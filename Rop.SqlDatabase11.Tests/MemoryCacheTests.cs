using Rop.Database11.CacheRepository;
using Rop.Results9;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Tests for MemoryCache (SD-2, SD-6): Expires must ignore missing keys
/// instead of throwing KeyNotFoundException; the cleanup event must be
/// resilient to faulty handlers and the cache must be disposable.
/// </summary>
public class MemoryCacheTests
{
    private static MemoryCache<int, ItemIntAuto> NewCache() => new(
        item => item.Id,
        TimeSpan.FromMinutes(5),
        TimeSpan.FromHours(1), // long cleanup interval so the timer does not interfere with the tests
        null);

    [Fact]
    public void Expires_With_Missing_Keys_Does_Not_Throw()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });
        cache.Add(new ItemIntAuto { Id = 2, Name = "B", Quantity = 2 });

        // Key 3 does not exist: it reproduces the change tracking INSERTs
        // that report keys not yet in the cache.
        var expired = cache.Expires(new[] { 1, 3 });

        Assert.Equal(new[] { 1 }, expired);
        var item1 = cache.GetValueOrDefault(1);
        Assert.True(item1 is ExpiredItem);
        Assert.Null(cache.GetValueOrDefault(3));
        Assert.True(cache.GetValueOrDefault(2) is ValidItem<ItemIntAuto>);
    }

    [Fact]
    public void Expires_With_Empty_List_Returns_Empty()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });

        var expired = cache.Expires();

        Assert.Empty(expired);
        Assert.True(cache.GetValueOrDefault(1) is ValidItem<ItemIntAuto>);
    }

    [Fact]
    public void Expires_Marks_All_Existing_Keys_As_Expired()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });
        cache.Add(new ItemIntAuto { Id = 2, Name = "B", Quantity = 2 });

        var expired = cache.Expires(new[] { 1, 2 });

        Assert.Equal(new[] { 1, 2 }, expired);
        Assert.True(cache.GetValueOrDefault(1) is ExpiredItem);
        Assert.True(cache.GetValueOrDefault(2) is ExpiredItem);
    }

    // U-2: TryGetValue returns the state as a union and transitions on expiry:
    // ValidItem -> JustExpiredItem (first access after expiry) -> ExpiredItem.
    [Fact]
    public void TryGetValue_Returns_States_And_Transitions_On_Expiry()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 }, TimeSpan.FromMilliseconds(50));

        Assert.True(cache.TryGetValue(1) is ValidItem<ItemIntAuto>);

        Thread.Sleep(150); // let the expiration elapse

        Assert.True(cache.TryGetValue(1) is JustExpiredItem);
        Assert.True(cache.TryGetValue(1) is ExpiredItem);

        Assert.Null(cache.TryGetValue(99)); // nonexistent key
    }

    // SD-6: a handler that throws must not prevent the other handlers from
    // running nor propagate the exception (Timer callback on a background
    // thread = process crash).
    [Fact]
    public void OnCacheCleanUp_Handler_Throwing_Does_Not_Stop_Other_Handlers()
    {
        using var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });

        var secondHandlerKeys = new List<int>();
        cache.CacheCleanUp += (_, _) => throw new InvalidOperationException("handler roto");
        cache.CacheCleanUp += (_, args) => secondHandlerKeys.AddRange(args.CleanUpKeys.Cast<int>());

        // Expires is the public path that triggers CacheCleanUp without relying on the timer.
        cache.Expires(new[] { 1 });

        Assert.Equal(new[] { 1 }, secondHandlerKeys);
    }

    // SD-6: Dispose does not throw and is idempotent.
    [Fact]
    public void Dispose_Does_Not_Throw_And_Is_Idempotent()
    {
        var cache = NewCache();
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 });

        cache.Dispose();
        cache.Dispose();
    }

    // SD-6: after Dispose the timer stops firing the cleanup event.
    [Fact]
    public void Dispose_Stops_Timer()
    {
        var cache = new MemoryCache<int, ItemIntAuto>(
            item => item.Id,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMilliseconds(10),
            null);

        var cleanupCount = 0;
        // The handler feeds the cache back with a short-lived item so the timer
        // always has something expired to clean up (otherwise, after the first
        // expiration the event would stop firing and the test would see nothing).
        cache.CacheCleanUp += (_, _) =>
        {
            Interlocked.Increment(ref cleanupCount);
            cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 }, TimeSpan.FromMilliseconds(20));
        };
        cache.Add(new ItemIntAuto { Id = 1, Name = "A", Quantity = 1 }, TimeSpan.FromMilliseconds(20));

        // Generous wait for the timer to fire at least a couple of times.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref cleanupCount) < 2 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }
        Assert.True(Volatile.Read(ref cleanupCount) >= 2, "the timer should have fired the cleanup before Dispose");

        cache.Dispose();

        // Margin for an in-flight tick to finish, then the counter is frozen.
        Thread.Sleep(200);
        var frozenCount = Volatile.Read(ref cleanupCount);
        Thread.Sleep(300);
        Assert.Equal(frozenCount, Volatile.Read(ref cleanupCount));
    }

    // SD-4: the DB query (RefreshAll callback) must not run under the lock.
    [Fact]
    public void RefreshAll_Does_Not_Hold_Lock_During_Refresh_Callback()
    {
        var cache = NewCache();
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);

        var refresh = Task.Run(() => cache.RefreshAll((_, _) =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return new EnumerableResult<ItemIntAuto>(new List<ItemIntAuto>());
        }));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        // While the reload is blocked, writing to the cache must complete.
        var addTask = Task.Run(() => cache.Add(new ItemIntAuto { Id = 9, Name = "X", Quantity = 1 }));
        Assert.True(addTask.Wait(TimeSpan.FromSeconds(2))); // previously it was blocked by the lock

        release.Set();
        Assert.True(refresh.Wait(TimeSpan.FromSeconds(5)));
        var name = cache.GetValueOrDefault(9) switch
        {
            ValidItem<ItemIntAuto> vi => vi.Value.Name,
            _ => throw new InvalidOperationException("se esperaba un item valido")
        };
        Assert.Equal("X", name);
    }
}
