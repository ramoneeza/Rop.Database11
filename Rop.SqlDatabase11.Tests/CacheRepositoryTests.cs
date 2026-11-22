using Dapper;
using Microsoft.Data.SqlClient;
using Rop.Dapper.ContribEx11;
using Rop.Database11.CacheRepository;
using Rop.Database11.Repository;
using Rop.Results9;
// Alias with a different name: "Database" would bind to the Rop.Database namespace (a member of the parent Rop)
// and using directives/aliases cannot beat a member of the containing namespace.
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Tests for the cache repository (SD-3): when the reload fails, GetAll
/// must return the in-memory data (the lesser evil) and report the error by out,
/// instead of throwing NRE. Uses a real Database against the LocalDB fixture.
/// </summary>
[Collection("Database")]
public class CacheRepositoryTests : IDisposable
{
    private readonly SqlConnection _connection;

    public CacheRepositoryTests(TestDatabaseFixture database)
    {
        _connection = database.OpenConnection();
        TestTables.CreateItemsIntAuto(_connection);
        _connection.InsertOrUpdate(new ItemIntAuto { Name = "A", Quantity = 1 });
        _connection.InsertOrUpdate(new ItemIntAuto { Name = "B", Quantity = 2 });
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Dispose_Is_Idempotent_And_Keeps_Database_Usable()
    {
        // SD-17: disposing the cache repo (MemoryCache with timer) is
        // idempotent and does NOT dispose the shared Database.
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewWorkingRepo(db);
        repo.Init();

        repo.Dispose();
        repo.Dispose();

        var all = db.GetAll<ItemIntAuto>();
        Assert.False(all.IsFailed);
    }

    private sealed class TestRepo : AbsSimpleCacheSqlRepositoryK<ItemIntAuto, ItemIntAuto, int>
    {
        public Func<EnumerableResult<ItemIntAuto>> ReloadAll { get; set; }
        public Func<IEnumerable<int>, EnumerableResult<ItemIntAuto>> ReloadSome { get; set; }

        public TestRepo(SqlDb db, Func<EnumerableResult<ItemIntAuto>> reloadAll, Func<IEnumerable<int>, EnumerableResult<ItemIntAuto>> reloadSome)
            : base(db, TimeSpan.FromHours(1), TimeSpan.FromHours(1))
        {
            ReloadAll = reloadAll;
            ReloadSome = reloadSome;
        }

        protected override ItemIntAuto Map(ItemIntAuto item) => item;
        protected override EnumerableResult<ItemIntAuto> IntReloadAll() => ReloadAll();
        protected override EnumerableResult<ItemIntAuto> IntReloadSome(params IEnumerable<int> keys) => ReloadSome(keys);
    }

    /// <summary>A different view of the DTO to test repos with T != D (SD-13).</summary>
    private sealed class ItemIntView
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class DtoRepo : AbsSimpleCacheSqlRepositoryK<ItemIntView, ItemIntAuto, int>
    {
        public DtoRepo(SqlDb db) : base(db, TimeSpan.FromHours(1), TimeSpan.FromHours(1))
        {
        }

        protected override ItemIntView Map(ItemIntAuto item) => new() { Id = item.Id, Name = item.Name };
        protected override EnumerableResult<ItemIntView> IntReloadAll() => throw new NotSupportedException();
        protected override EnumerableResult<ItemIntView> IntReloadSome(params IEnumerable<int> keys) => throw new NotSupportedException();
    }

    private static TestRepo NewWorkingRepo(SqlDb db) => new(
        db,
        () => db.Query<ItemIntAuto>("SELECT * FROM ItemsIntAuto"),
        keys => db.Query<ItemIntAuto>($"SELECT * FROM ItemsIntAuto WHERE Id IN ({string.Join(",", keys)})"));

    private static TestRepo NewFailingRepo(SqlDb db) => new(
        db,
        () => Error.Exception(new Exception("boom")),
        _ => Error.Exception(new Exception("boom")));

    [Fact]
    public void GetAll_Success_Returns_Items()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewWorkingRepo(db);
        repo.Init();

        var r = repo.GetAll(out var error);

        Assert.Null(error);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void GetAll_When_Reload_Fails_Returns_Memory_Data_Without_Throwing()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewFailingRepo(db);
        repo.Init();

        // The lesser evil: return whatever remains in memory (nothing here,
        // it never loaded) and report the error by out, without throwing NRE.
        var r = repo.GetAll(out var error);

        Assert.NotNull(error);
        Assert.Empty(r);
    }

    [Fact]
    public void GetAllDictionary_When_Reload_Fails_Returns_Memory_Data_With_Error()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewFailingRepo(db);
        repo.Init();

        var r = repo.GetAllDictionary(out var error);

        Assert.NotNull(error);
        Assert.Empty(r);
    }

    [Fact]
    public void GetAllDictionary_Success_Returns_Items()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewWorkingRepo(db);
        repo.Init();

        var r = repo.GetAllDictionary(out var error);

        Assert.Null(error);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Get_Returns_Item()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewWorkingRepo(db);
        repo.Init();

        var r = repo.Get(1, out var error);

        Assert.Null(error);
        Assert.Equal("A", r!.Name);
    }

    [Fact]
    public void Get_Missing_Key_Returns_Null_Without_Error()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewWorkingRepo(db);
        repo.Init();

        var r = repo.Get(999, out var error);

        Assert.Null(r);
        Assert.Null(error);
    }

    [Fact]
    public void Get_When_Reload_Fails_Returns_Error()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewFailingRepo(db);
        repo.Init();

        var r = repo.Get(1, out var error);

        Assert.Null(r);
        Assert.NotNull(error); // a real error, not "not found"
    }

    [Fact]
    public void GetSome_Returns_Items_And_Skips_Missing_Keys()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewWorkingRepo(db);
        repo.Init();

        var r = repo.GetSome(new[] { 1, 999, 2 }, out var error);

        Assert.Null(error);
        Assert.Equal(new[] { 1, 2 }, r.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void GetSome_When_Reload_Fails_Returns_Error()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewFailingRepo(db);
        repo.Init();

        var r = repo.GetSome(new[] { 1 }, out var error);

        Assert.NotNull(error);
        Assert.Empty(r);
    }

    [Fact]
    public void GetDKey_With_Dto_Uses_Dto_Key_Property()
    {
        // SD-13: GetDKey used the T-type property on a D instance
        // (TargetException when T != D).
        var db = new SqlDb(_connection.ConnectionString);
        var repo = new DtoRepo(db);

        var key = repo.GetDKey(new ItemIntAuto { Id = 42, Name = "X", Quantity = 1 });

        Assert.Equal(42, key);
    }

    [Fact]
    public async Task GetAll_Does_Not_Block_Get_Of_Cached_Items_During_Reload()
    {
        // SD-4: the reload (DB) must not block reads of items already in the cache.
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewWorkingRepo(db);
        repo.Init();
        Assert.Equal(2, repo.GetAll(out var e0).Count);
        Assert.Null(e0);

        // Both reloads blocked: so RefreshAll always hits the block
        // (whether IntReloadAll or IntReloadSome, depending on the expired-key threshold).
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<EnumerableResult<ItemIntAuto>> blocking = () =>
        {
            started.TrySetResult();
            release.Task.Wait(TimeSpan.FromSeconds(5));
            return db.Query<ItemIntAuto>("SELECT * FROM ItemsIntAuto");
        };
        repo.ReloadAll = blocking;
        repo.ReloadSome = _ => blocking();

        var getAllTask = Task.Run(() => repo.GetAll(out _));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var getTask = Task.Run(() => repo.Get(1, out _));
        var completed = await Task.WhenAny(getTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(getTask, completed); // the cache read must not wait for the reload

        release.TrySetResult();
        await getAllTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ==== CacheRepository richness (DTO/string and ISqlRepository variants) ====

    private sealed class SimpleIntDtoRepo : AbsSimpleCacheSqlIntRepository<ItemIntView, ItemIntAuto>
    {
        public SimpleIntDtoRepo(SqlDb db) : base(db, TimeSpan.FromHours(1), TimeSpan.FromHours(1)) { }
        protected override ItemIntView Map(ItemIntAuto item) => new() { Id = item.Id, Name = item.Name };
        protected override EnumerableResult<ItemIntView> IntReloadAll() => throw new NotSupportedException();
        protected override EnumerableResult<ItemIntView> IntReloadSome(params IEnumerable<int> keys) => throw new NotSupportedException();
    }

    private sealed class SimpleStringDtoRepo : AbsSimpleCacheSqlRepository<ItemStringKey, ItemStringKey>
    {
        public SimpleStringDtoRepo(SqlDb db) : base(db, TimeSpan.FromHours(1), TimeSpan.FromHours(1)) { }
        protected override ItemStringKey Map(ItemStringKey item) => item;
        protected override EnumerableResult<ItemStringKey> IntReloadAll() => throw new NotSupportedException();
        protected override EnumerableResult<ItemStringKey> IntReloadSome(params IEnumerable<string> keys) => throw new NotSupportedException();
    }

    [Fact]
    public void Extension_Indexer_Reads_Item_By_Key()
    {
        // EI-1: repo[key] as sugar over Get(key, out _) (extension indexer).
        var db = new SqlDb(_connection.ConnectionString);
        var repo = NewWorkingRepo(db);
        repo.Init();

        ItemIntAuto? item = repo[1];

        Assert.NotNull(item);
        Assert.Equal("A", item.Name);
        Assert.Null(repo[999]);
    }

    [Fact]
    public void Simple_Int_Dto_Repo_Exposes_Keys_And_Simple_Interface()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = new SimpleIntDtoRepo(db);

        Assert.IsAssignableFrom<IAbsSimpleSqlRepository<ItemIntView, int>>(repo);
        Assert.Equal(42, repo.GetDKey(new ItemIntAuto { Id = 42, Name = "X", Quantity = 1 }));
        Assert.Equal(7, repo.GetTKey(new ItemIntView { Id = 7 }));
    }

    [Fact]
    public void Simple_String_Dto_Repo_Exposes_Keys_And_Simple_Interface()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = new SimpleStringDtoRepo(db);

        Assert.IsAssignableFrom<IAbsSimpleSqlRepository<ItemStringKey, string>>(repo);
        Assert.Equal("K1", repo.GetDKey(new ItemStringKey { Id = "K1", Name = "X" }));
        Assert.Equal("K2", repo.GetTKey(new ItemStringKey { Id = "K2", Name = "Y" }));
    }

}
