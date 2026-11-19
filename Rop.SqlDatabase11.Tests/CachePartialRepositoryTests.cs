using Microsoft.Data.SqlClient;
using Rop.Dapper.ContribEx11;
using Rop.Database11.CachePartialKeyRepository;
using Rop.Database11.PartialKeyRepository;
using Rop.Results9;
// Alias con nombre distinto: "Database" enlazaría al namespace Rop.Database.
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Tests for the partial key repositories with cache expiration (tablas SIN
/// change tracking: la expiración TTL es el mecanismo de invalidación).
/// </summary>
[Collection("Database")]
public class CachePartialRepositoryTests : IDisposable
{
    private readonly SqlConnection _connection;

    public CachePartialRepositoryTests(TestDatabaseFixture database)
    {
        _connection = database.OpenConnection();
        TestTables.CreateItemsPartial(_connection);
        _connection.InsertNoKeyAtt(new ItemPartial { OwnerId = 1, Code = "A", Quantity = 1 });
        _connection.InsertNoKeyAtt(new ItemPartial { OwnerId = 1, Code = "B", Quantity = 2 });
        _connection.InsertNoKeyAtt(new ItemPartial { OwnerId = 2, Code = "C", Quantity = 3 });
    }

    public void Dispose() => _connection.Dispose();

    private sealed class CachePartialRepo : AbsSimpleCacheSqlPartialIntRepository<ItemPartial>
    {
        private readonly SqlDb _db;
        public int ReloadCount;

        public CachePartialRepo(SqlDb db, TimeSpan? expiration = null)
            : base(db, expiration, TimeSpan.FromHours(1))
        {
            _db = db;
        }

        protected override EnumerableResult<ItemPartial> IntReloadAllPartial()
        {
            ReloadCount++;
            return _db.GetAllNoKey<ItemPartial>();
        }

        protected override EnumerableResult<ItemPartial> IntReloadSomePartial(params IEnumerable<int> keys)
        {
            ReloadCount++;
            var all = new List<ItemPartial>();
            foreach (var k in keys)
            {
                var r = _db.GetPartial<ItemPartial>(k);
                if (r.IsFailed) return r.Error!;
                all.AddRange(r.Value);
            }
            return all;
        }
    }

    [Fact]
    public void Groups_Items_By_First_Key_And_Gets_Blocks()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = new CachePartialRepo(db);
        repo.Init();

        var all = repo.GetAll(out var error);

        Assert.Null(error);
        Assert.Equal(2, all.Count); // bloques por OwnerId: 1 y 2
        var block1 = all.Single(b => b.Key == 1);
        Assert.Equal(new[] { "A", "B" }, block1.Block.Select(i => i.Code).ToArray());

        var block = repo.Get(1, out var error2);
        Assert.Null(error2);
        Assert.Equal(2, block!.Block.Count);

        // GetKey del core extrae la primera clave parcial
        Assert.Equal(1, repo.GetKey(new ItemPartial { OwnerId = 1, Code = "X" }));
    }

    [Fact]
    public void Expired_Entries_Are_Reloaded_From_Database()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = new CachePartialRepo(db, TimeSpan.FromMilliseconds(50));
        repo.Init();

        var first = repo.GetAll(out var error1);
        Assert.Null(error1);
        Assert.Equal(2, first.Count);
        Assert.Equal(1, repo.ReloadCount);

        Thread.Sleep(150); // deja expirar las entradas

        var second = repo.GetAll(out var error2);

        Assert.Null(error2);
        Assert.Equal(2, second.Count);
        Assert.Equal(2, repo.ReloadCount); // recargada desde BD al expirar
    }
}
