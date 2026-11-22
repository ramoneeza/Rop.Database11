using Rop.Database11.CycleTask;
using Rop.Results9;

namespace Rop.SqlDatabase11.ChangeTracking.Tests;

/// <summary>
/// SD-5: CycleTaskService.TryAdd must be thread-safe: the same key always
/// returns the same instance, and concurrent registration does not corrupt
/// the map nor start duplicate polling loops.
/// </summary>
public class CycleTaskServiceTests
{
    private sealed class FakeTask : ICycleTask
    {
        public string Key { get; }
        public TimeSpan Interval => TimeSpan.FromSeconds(60);
        public FakeTask(string key) => Key = key;
        public VoidResult PayLoad(CancellationToken cancellationToken = default) => VoidResult.Ok;
    }

    private sealed class DisposableFakeTask : ICycleTask, IDisposable
    {
        public TimeSpan Interval => TimeSpan.FromSeconds(60);
        public bool IsDisposed { get; private set; }
        public VoidResult PayLoad(CancellationToken cancellationToken = default) => VoidResult.Ok;
        public void Dispose() => IsDisposed = true;
    }

    private sealed class CancellingFakeTask : ICycleTask
    {
        public TimeSpan Interval => TimeSpan.FromSeconds(60);
        public VoidResult PayLoad(CancellationToken cancellationToken = default) => throw new OperationCanceledException(cancellationToken);
    }

    [Fact]
    public async Task TryAdd_Same_Key_Concurrently_Returns_Same_Instance()
    {
        using var service = new CycleTaskService<string, FakeTask>();
        var gate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() =>
            {
                gate.Wait();
                return service.TryAdd("k", _ => new FakeTask("k"));
            }))
            .ToArray();

        gate.Set();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async Task TryAdd_Different_Keys_Concurrently_Does_Not_Throw()
    {
        using var service = new CycleTaskService<string, FakeTask>();
        var gate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 64)
            .Select(i => Task.Run(() =>
            {
                gate.Wait();
                return service.TryAdd($"k{i}", k => new FakeTask(k));
            }))
            .ToArray();

        gate.Set();
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
            Assert.Equal($"k{i}", results[i].Key);
    }

    [Fact]
    public void Dispose_Disposes_Disposable_Tasks()
    {
        // SD-17: disposing the service also disposes its IDisposable items
        // (e.g. SqlTableDependency with its channel).
        var service = new CycleTaskService<string, DisposableFakeTask>();
        var fake = service.TryAdd("k", _ => new DisposableFakeTask());

        service.Dispose();

        Assert.True(fake.IsDisposed);
    }

    [Fact]
    public void Wrapper_Rethrows_OperationCanceledException()
    {
        // GE-4: the OCE from PayLoad propagates up to the service loop (clean
        // shutdown); it is not turned into an error message.
        var wrapper = new CycleTaskWrapper<CancellingFakeTask>(new CancellingFakeTask());

        Assert.ThrowsAny<OperationCanceledException>(() => wrapper.Execute());
    }

    [Fact]
    public void Wrapper_Returns_Success_When_Token_Cancelled()
    {
        // CH-1: with VoidResult, "not executed" is no longer null, it is success.
        var wrapper = new CycleTaskWrapper<FakeTask>(new FakeTask("k"));

        Assert.False(wrapper.Execute(new CancellationToken(true)).IsFailed);
    }

    [Fact]
    public void Wrapper_Execute_Uses_Utc_Timestamps()
    {
        // SD-14: consistent UTC instants in the scheduler (it used to mix
        // DateTimeOffset.Now and DateTime.Now).
        var wrapper = new CycleTaskWrapper<FakeTask>(new FakeTask("k"));

        Assert.False(wrapper.Execute().IsFailed);

        Assert.Equal(TimeSpan.Zero, wrapper.LastRun.Offset);
        Assert.Equal(TimeSpan.Zero, wrapper.NextRun.Offset);
        Assert.True(wrapper.NextRun > wrapper.LastRun);
    }

    [Fact]
    public void Queue_TryDequeueInTime_Respects_Future_NextRun()
    {
        // SD-14: the queue compares NextRun (UTC) against UtcNow; a wrapper
        // just executed with a 60s interval must not be due yet.
        var queue = new CycleQueue<FakeTask>();
        queue.Enqueue(new FakeTask("k"));
        Assert.True(queue.TryDequeueInTime(out var wrapper));

        wrapper!.Execute(); // NextRun = UtcNow + 60s
        queue.Enqueue(wrapper);

        Assert.False(queue.TryDequeueInTime(out _));
    }
}
