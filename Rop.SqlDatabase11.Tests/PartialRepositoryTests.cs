using Microsoft.Data.SqlClient;
using Rop.Dapper.ContribEx11;
using Rop.Database11.PartialKeyRepository;
using Rop.Database11.Repository;
using Rop.Results9;
// Alias with a different name: "Database" would bind to the Rop.Database namespace.
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Tests for the partial key repositories (they group items by the first partial key).
/// PartialKeyRepository richness (2026-11-19): simple no-DTO variants with identity
/// Map and no-DTO CT variants with virtual GetKey and ISqlRepository.
/// </summary>
[Collection("Database")]
public class PartialRepositoryTests : IDisposable
{
    private readonly SqlConnection _connection;

    public PartialRepositoryTests(TestDatabaseFixture database)
    {
        _connection = database.OpenConnection();
        TestTables.CreateItemsPartial(_connection);
        _connection.InsertNoKeyAtt(new ItemPartial { OwnerId = 1, Code = "A", Quantity = 1 });
        _connection.InsertNoKeyAtt(new ItemPartial { OwnerId = 1, Code = "B", Quantity = 2 });
        _connection.InsertNoKeyAtt(new ItemPartial { OwnerId = 2, Code = "C", Quantity = 3 });
    }

    public void Dispose() => _connection.Dispose();

    private sealed class SimplePartialRepo : AbsSimpleSqlPartialIntRepository<ItemPartial>
    {
        private readonly SqlDb _db;
        public SimplePartialRepo(SqlDb db) : base(db)
        {
            _db = db;
        }

        protected override EnumerableResult<ItemPartial> IntReloadAllPartial() => _db.GetAllNoKey<ItemPartial>();
        protected override EnumerableResult<ItemPartial> IntReloadSomePartial(params IEnumerable<int> keys)
        {
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

    private sealed class CtPartialRepo : AbsSqlPartialIntRepository<ItemPartial>
    {
        public CtPartialRepo(SqlDb db) : base(db)
        {
        }
        public override int CombineKeys(object? key1, object? key2) => (int)(key1 ?? 0);
    }

    [Fact]
    public void Simple_NoDto_Partial_Repo_Groups_Items_By_First_Key()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = new SimplePartialRepo(db);
        repo.Init();

        var all = repo.GetAll(out var error);

        Assert.Null(error);
        Assert.Equal(2, all.Count); // blocks by OwnerId: 1 and 2
        var block1 = all.Single(b => b.Key == 1);
        Assert.Equal(new[] { "A", "B" }, block1.Block.Select(i => i.Code).ToArray());

        var block = repo.Get(1, out var error2);
        Assert.Null(error2);
        Assert.Equal(2, block!.Block.Count);

        // The core GetKey (on DTO == entity) extracts the first partial key
        Assert.Equal(1, repo.GetKey(new ItemPartial { OwnerId = 1, Code = "X" }));
    }

    [Fact]
    public void Ct_NoDto_Partial_Repo_Is_SqlRepository_And_Gets_Blocks()
    {
        var db = new SqlDb(_connection.ConnectionString);
        var repo = new CtPartialRepo(db);

        Assert.IsAssignableFrom<ISqlRepository<PartialBlock<int, ItemPartial>, int>>(repo);
        Assert.NotNull(repo.Version);

        repo.Init();
        var block = repo.Get(2, out var error);

        Assert.Null(error);
        Assert.Single(block!.Block);
        Assert.Equal("C", block.Block[0].Code);

        // The virtual GetKey of the no-DTO variant
        Assert.Equal(2, repo.GetKey(new ItemPartial { OwnerId = 2, Code = "X" }));
    }
}
