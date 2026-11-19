using Rop.Database11.CycleTask;

namespace Rop.SqlDatabase11.ChangeTracking.Tests;

/// <summary>
/// SD-5: CycleTaskService.TryAdd debe ser thread-safe: la misma clave
/// devuelve siempre la misma instancia y el alta concurrente no corrompe
/// el mapa ni arranca dobles bucles de polling.
/// </summary>
public class CycleTaskServiceTests
{
    private sealed class FakeTask : ICycleTask
    {
        public string Key { get; }
        public TimeSpan Interval => TimeSpan.FromSeconds(60);
        public FakeTask(string key) => Key = key;
        public string? PayLoad(CancellationToken cancellationToken = default) => null;
    }

    private sealed class DisposableFakeTask : ICycleTask, IDisposable
    {
        public TimeSpan Interval => TimeSpan.FromSeconds(60);
        public bool IsDisposed { get; private set; }
        public string? PayLoad(CancellationToken cancellationToken = default) => null;
        public void Dispose() => IsDisposed = true;
    }

    private sealed class CancellingFakeTask : ICycleTask
    {
        public TimeSpan Interval => TimeSpan.FromSeconds(60);
        public string? PayLoad(CancellationToken cancellationToken = default) => throw new OperationCanceledException(cancellationToken);
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
        // SD-17: al disponer el servicio se disponen los items IDisposable
        // (p. ej. SqlTableDependency con su canal).
        var service = new CycleTaskService<string, DisposableFakeTask>();
        var fake = service.TryAdd("k", _ => new DisposableFakeTask());

        service.Dispose();

        Assert.True(fake.IsDisposed);
    }

    [Fact]
    public void Wrapper_Rethrows_OperationCanceledException()
    {
        // GE-4: la OCE de PayLoad sube al bucle del servicio (parada limpia),
        // no se convierte en mensaje de error.
        var wrapper = new CycleTaskWrapper<CancellingFakeTask>(new CancellingFakeTask());

        Assert.ThrowsAny<OperationCanceledException>(() => wrapper.Execute());
    }

    [Fact]
    public void Wrapper_Returns_Null_When_Token_Cancelled()
    {
        var wrapper = new CycleTaskWrapper<FakeTask>(new FakeTask("k"));

        Assert.Null(wrapper.Execute(new CancellationToken(true)));
    }

    [Fact]
    public void Wrapper_Execute_Uses_Utc_Timestamps()
    {
        // SD-14: instantes UTC consistentes en el scheduler (antes se mezclaban
        // DateTimeOffset.Now y DateTime.Now).
        var wrapper = new CycleTaskWrapper<FakeTask>(new FakeTask("k"));

        Assert.Null(wrapper.Execute());

        Assert.Equal(TimeSpan.Zero, wrapper.LastRun.Offset);
        Assert.Equal(TimeSpan.Zero, wrapper.NextRun.Offset);
        Assert.True(wrapper.NextRun > wrapper.LastRun);
    }

    [Fact]
    public void Queue_TryDequeueInTime_Respects_Future_NextRun()
    {
        // SD-14: la cola compara NextRun (UTC) contra UtcNow; un wrapper
        // recien ejecutado con intervalo de 60s no debe salir todavia.
        var queue = new CycleQueue<FakeTask>();
        queue.Enqueue(new FakeTask("k"));
        Assert.True(queue.TryDequeueInTime(out var wrapper));

        wrapper!.Execute(); // NextRun = UtcNow + 60s
        queue.Enqueue(wrapper);

        Assert.False(queue.TryDequeueInTime(out _));
    }
}
