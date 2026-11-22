using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.SqlClient;
using Rop.Dapper.ContribEx11;
using Rop.Results9;
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Tests for the AbsDatabase extension wrappers (AbsDatabase.ext.cs) and the
/// remaining overloads, exercised through the concrete Database against a
/// real SQL Server database.
/// </summary>
[Collection("Database")]
public class DatabaseExtTests : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlDb _db;

    public DatabaseExtTests(TestDatabaseFixture database)
    {
        _connection = database.OpenConnection();
        TestTables.CreateItemsIntAuto(_connection);
        TestTables.CreateItemsStringKey(_connection);
        TestTables.CreateItemsPartial(_connection);
        TestTables.CreateLogEntries(_connection);
        Seed();
        _db = new SqlDb(_connection.ConnectionString);
    }

    public void Dispose() => _connection.Dispose();

    private void Seed()
    {
        _connection.Execute("INSERT INTO ItemsIntAuto (Name, Quantity) VALUES ('A', 1)");
        _connection.Execute("INSERT INTO ItemsStringKey (Id, Name, Quantity) VALUES ('K-1', 'A', 1)");
        _connection.Execute("""
            INSERT INTO ItemsPartial (OwnerId, Code, Quantity) VALUES
            (1, 'A', 10), (1, 'B', 20), (2, 'C', 30);
            """);
    }

    // ==== InsertNoKeyAtt ====

    [Fact]
    public void InsertNoKeyAtt_Inserts_Row()
    {
        var r = _db.InsertNoKeyAtt(new LogEntry { Message = "x", Severity = 2 });

        Assert.False(r.IsFailed);
        Assert.Equal(1, r.Value);
    }

    [Fact]
    public async Task InsertNoKeyAttAsync_Inserts_Row()
    {
        var r = await _db.InsertNoKeyAttAsync(new LogEntry { Message = "x", Severity = 2 });

        Assert.False(r.IsFailed);
        Assert.Equal(1, r.Value);
    }

    // ==== InsertOrUpdateMerge ====

    [Fact]
    public void InsertOrUpdateMerge_Inserts_Then_Updates()
    {
        var ins = _db.InsertOrUpdateMerge(new ItemStringKey { Id = "K-3", Name = "C", Quantity = 3 });
        Assert.False(ins.IsFailed);

        var upd = _db.InsertOrUpdateMerge(new ItemStringKey { Id = "K-3", Name = "C2", Quantity = 4 });

        Assert.False(upd.IsFailed);
        var item = _connection.Get<ItemStringKey>("K-3");
        Assert.Equal("C2", item.Name);
    }

    [Fact]
    public async Task InsertOrUpdateMergeAsync_Inserts()
    {
        var r = await _db.InsertOrUpdateMergeAsync(new ItemStringKey { Id = "K-4", Name = "D", Quantity = 5 });

        Assert.False(r.IsFailed);
        Assert.Equal("D", _connection.Get<ItemStringKey>("K-4").Name);
    }

    // ==== InsertOrUpdatePartial ====

    [Table("NoExisteTabla")]
    private sealed class NoTablePartial
    {
        [PartialKey(0)]
        public int OwnerId { get; set; }

        [PartialKey(1)]
        public string Code { get; set; } = "";

        public int Quantity { get; set; }
    }

    [Fact]
    public void InsertOrUpdatePartial_Sync_Fails_When_Table_Missing()
    {
        // AB-5: the sync version used to always return true; it now propagates
        // the failure just like the async one.
        var r = _db.InsertOrUpdatePartial<NoTablePartial>(1, new[]
        {
            new NoTablePartial { OwnerId = 1, Code = "A", Quantity = 1 }
        });

        Assert.True(r.IsFailed);
    }

    [Fact]
    public async Task InsertOrUpdatePartialAsync_Fails_When_Table_Missing()
    {
        var r = await _db.InsertOrUpdatePartialAsync<NoTablePartial>(1, new[]
        {
            new NoTablePartial { OwnerId = 1, Code = "A", Quantity = 1 }
        });

        Assert.True(r.IsFailed);
    }

    [Fact]
    public void InsertOrUpdatePartial_Replaces_Rows()
    {
        var r = _db.InsertOrUpdatePartial<ItemPartial>(1, new[]
        {
            new ItemPartial { OwnerId = 1, Code = "A", Quantity = 100 },
            new ItemPartial { OwnerId = 1, Code = "D", Quantity = 400 }
        });

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        var rows = _connection.Query<ItemPartial>("SELECT * FROM ItemsPartial WHERE OwnerId=1").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, p => p.Code == "D");
        Assert.DoesNotContain(rows, p => p.Code == "B");
    }

    [Fact]
    public async Task InsertOrUpdatePartialAsync_Replaces_Rows()
    {
        var r = await _db.InsertOrUpdatePartialAsync<ItemPartial>(2, new[]
        {
            new ItemPartial { OwnerId = 2, Code = "E", Quantity = 500 }
        });

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        var row = Assert.Single(_connection.Query<ItemPartial>("SELECT * FROM ItemsPartial WHERE OwnerId=2"));
        Assert.Equal("E", row.Code);
    }

    // ==== DeleteByPartialKey ====

    [Fact]
    public void DeleteByPartialKey_Removes_Rows()
    {
        var r = _db.DeleteByPartialKey<ItemPartial>(1);

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value);
        Assert.Empty(_connection.Query<ItemPartial>("SELECT * FROM ItemsPartial WHERE OwnerId=1"));
    }

    [Fact]
    public async Task DeleteByPartialKeyAsync_Removes_Rows()
    {
        var r = await _db.DeleteByPartialKeyAsync<ItemPartial>(2);

        Assert.False(r.IsFailed);
        Assert.Equal(1, r.Value);
        Assert.Empty(_connection.Query<ItemPartial>("SELECT * FROM ItemsPartial WHERE OwnerId=2"));
    }

    // ==== GetPartial / GetPartial2 ====

    [Fact]
    public void GetPartial_By_Two_Keys_Returns_Item()
    {
        var r = _db.GetPartial<ItemPartial>(1, "A");

        Assert.False(r.IsFailed);
        Assert.Equal(10, r.Value!.Quantity);
    }

    [Fact]
    public async Task GetPartialAsync_By_Two_Keys_Returns_Item()
    {
        var r = await _db.GetPartialAsync<ItemPartial>(1, "B");

        Assert.False(r.IsFailed);
        Assert.Equal(20, r.Value!.Quantity);
    }

    [Fact]
    public void GetPartial_By_First_Key_Returns_Items()
    {
        var r = _db.GetPartial<ItemPartial>(1);

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    [Fact]
    public async Task GetPartialAsync_By_First_Key_Returns_Items()
    {
        var r = await _db.GetPartialAsync<ItemPartial>(1);

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    [Fact]
    public void GetPartial2_By_Second_Key_Returns_Items()
    {
        var r = _db.GetPartial2<ItemPartial>("C");

        Assert.False(r.IsFailed);
        Assert.Equal(30, Assert.Single(r.Value!).Quantity);
    }

    [Fact]
    public async Task GetPartial2Async_By_Second_Key_Returns_Items()
    {
        var r = await _db.GetPartial2Async<ItemPartial>("C");

        Assert.False(r.IsFailed);
        Assert.Single(r.Value!);
    }

    // ==== DeleteAsync with string key ====

    [Fact]
    public async Task DeleteAsync_By_String_Key_Removes_Row()
    {
        var r = await _db.DeleteAsync<ItemStringKey>("K-1");

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Null(_connection.Get<ItemStringKey>("K-1"));
    }

    // ==== UpdateIdValueAsync ====

    [Fact]
    public async Task UpdateIdValueAsync_Updates_Field()
    {
        var r = await _db.UpdateIdValueAsync<ItemIntAuto, int>(1, 42, "Quantity");

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Equal(42, _connection.Get<ItemIntAuto>(1).Quantity);
    }

    // ==== GetSomeSlim / GetSomeAsync with string key ====

    [Fact]
    public void GetSomeSlim_String_Ids_Returns_Found()
    {
        var r = _db.GetSomeSlim<ItemStringKey>(new[] { "K-1", "NO" });

        Assert.False(r.IsFailed);
        Assert.Equal("K-1", Assert.Single(r.Value!).Id);
    }

    [Fact]
    public async Task GetSomeSlimAsync_String_Ids_Returns_Found()
    {
        var r = await _db.GetSomeSlimAsync<ItemStringKey>(new[] { "K-1" });

        Assert.False(r.IsFailed);
        Assert.Single(r.Value!);
    }

    [Fact]
    public async Task GetSomeAsync_String_Ids_Returns_Found()
    {
        var r = await _db.GetSomeAsync<ItemStringKey>(new[] { "K-1", "NO" });

        Assert.False(r.IsFailed);
        Assert.Equal("K-1", Assert.Single(r.Value!).Id);
    }

    // ==== KeyDescription wrappers ====

    [Fact]
    public void KeyDescription_Wrappers_Return_Descriptions()
    {
        var single = _db.GetKeyDescription<ItemIntAuto>();
        Assert.NotNull(single);
        Assert.Equal("Id", single!.KeyName);

        var anySingle = _db.GetAnyKeyDescription<ItemIntAuto>();
        Assert.NotNull(anySingle);

        var partial = _db.GetPartialKeyDescription<ItemPartial>();
        Assert.NotNull(partial);
        Assert.Equal("OwnerId", partial!.KeyName);

        var anyPartial = _db.GetAnyKeyDescription<ItemPartial>();
        Assert.NotNull(anyPartial);
        Assert.Equal("OwnerId", anyPartial!.KeyName);
    }

    // ==== CancellationToken (AB-7 / GE-4) ====

    [Fact]
    public async Task GetAllSlimAsync_With_Cancelled_Token_Throws()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _db.GetAllSlimAsync<ItemIntAuto>(new CancellationToken(true)));
    }

    [Fact]
    public async Task GetSomeSlimAsync_With_Cancelled_Token_Throws()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _db.GetSomeSlimAsync<ItemIntAuto>(new[] { 1 }, new CancellationToken(true)));
    }

    [Fact]
    public async Task GetWhereSlimAsync_With_Cancelled_Token_Throws()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _db.GetWhereSlimAsync<ItemIntAuto>("1=1", null, new CancellationToken(true)));
    }

    [Fact]
    public async Task GetWhereAsync_With_Cancelled_Token_Throws()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _db.GetWhereAsync<ItemIntAuto>("1=1", null, new CancellationToken(true)));
    }
}
