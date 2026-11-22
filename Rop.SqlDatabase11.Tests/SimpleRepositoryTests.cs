using Rop.Database11.Repository;
using Rop.Results9;
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Tests for the non-cache repository (AbsSimpleSqlRepositoryK):
/// SD-10 — ResetIds must set MustReload=true when the reload fails,
/// so the next Check() retries the full load.
/// </summary>
[Collection("Database")]
public class SimpleRepositoryTests : IDisposable
{
    private readonly SqlDb _db;

    public SimpleRepositoryTests(TestDatabaseFixture database)
    {
        var connection = database.OpenConnection();
        _db = new SqlDb(connection.ConnectionString);
        connection.Dispose();
    }

    public void Dispose()
    {
    }

    private sealed class TestRepo : AbsSimpleSqlRepositoryK<ItemIntAuto, ItemIntAuto, int>
    {
        public new Func<EnumerableResult<ItemIntAuto>> ReloadAll { get; set; } = () => Error.Exception(new Exception("not set"));
        public Func<IEnumerable<int>, EnumerableResult<ItemIntAuto>> ReloadSome { get; set; } = _ => Error.Exception(new Exception("not set"));

        public TestRepo(SqlDb db) : base(db)
        {
        }

        protected override ItemIntAuto Map(ItemIntAuto item) => item;
        protected override EnumerableResult<ItemIntAuto> IntReloadAll() => ReloadAll();
        protected override EnumerableResult<ItemIntAuto> IntReloadSome(params IEnumerable<int> keys) => ReloadSome(keys);
    }

    private static EnumerableResult<ItemIntAuto> Items() => new(new List<ItemIntAuto>
    {
        new() { Id = 1, Name = "A", Quantity = 1 },
        new() { Id = 2, Name = "B", Quantity = 2 }
    });

    [Fact]
    public void ResetIds_When_Reload_Fails_Sets_MustReload()
    {
        var repo = new TestRepo(_db) { ReloadAll = Items };
        repo.Init();
        Assert.False(repo.MustReload);

        repo.ReloadSome = _ => Error.Exception(new Exception("boom"));
        var ok = repo.ResetIds(new[] { 1 });

        Assert.False(ok);
        Assert.NotNull(repo.LastError);
        Assert.True(repo.MustReload); // SD-10: retry on the next call
    }

    [Fact]
    public void ResetIds_Failure_Is_Recovered_By_Next_Check()
    {
        var reloadAllCalls = 0;
        var repo2 = new TestRepo(_db) { ReloadAll = () => { reloadAllCalls++; return Items(); } };
        repo2.Init();
        repo2.ReloadSome = _ => Error.Exception(new Exception("boom"));
        repo2.ResetIds(new[] { 1 });
        Assert.True(repo2.MustReload);

        repo2.Check();

        Assert.False(repo2.MustReload);
        Assert.Null(repo2.LastError);
        Assert.Equal(2, reloadAllCalls); // Init + retry
    }

    [Fact]
    public void ResetIds_Waits_For_Ongoing_ReloadAll()
    {
        // SD-9: ResetIds must wait for an ongoing ReloadAll to finish.
        var inReload = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var someCalls = 0;
        var repo = new TestRepo(_db)
        {
            ReloadAll = () =>
            {
                inReload.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return Items();
            },
            ReloadSome = _ =>
            {
                Interlocked.Increment(ref someCalls);
                return Items();
            }
        };

        var reloadTask = Task.Run(() => repo.Init());
        Assert.True(inReload.Wait(TimeSpan.FromSeconds(5)));

        var resetTask = Task.Run(() => repo.ResetIds(new[] { 1 }));
        Thread.Sleep(300);
        Assert.Equal(0, Volatile.Read(ref someCalls)); // it must be waiting

        release.Set();
        Assert.True(reloadTask.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(resetTask.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref someCalls));
    }

    [Fact]
    public void Dispose_Is_Idempotent_And_Keeps_Database_Usable()
    {
        // SD-17: disposing the repo (without CT) is idempotent and does NOT
        // dispose the shared Database.
        var repo = new TestRepo(_db) { ReloadAll = Items };
        repo.Init();

        repo.Dispose();
        repo.Dispose();

        var all = _db.GetAll<ItemIntAuto>();
        Assert.False(all.IsFailed);
    }
}
