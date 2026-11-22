using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.SqlClient;
using Rop.Dapper.ContribEx11;
using Rop.Results9;
// Alias: "Database" would bind to the Rop.Database namespace (a member of the parent Rop)
// and using directives/aliases cannot beat a member of the containing namespace.
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Tests for the concrete Database (SqlDatabase11). They exercise the
/// AbsDatabase CRUD/query wrappers against a real SQL Server database,
/// which is where the abstract class gets its real coverage.
/// </summary>
[Collection("Database")]
public class DatabaseTests : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlDb _db;

    public DatabaseTests(TestDatabaseFixture database)
    {
        _connection = database.OpenConnection();
        TestTables.CreateItemsIntAuto(_connection);
        TestTables.CreateItemsStringKey(_connection);
        TestTables.CreateLogEntries(_connection);
        TestTables.CreateOrders(_connection);
        Seed();
        _db = new SqlDb(_connection.ConnectionString);
    }

    public void Dispose() => _connection.Dispose();

    private void Seed()
    {
        _connection.Insert(new ItemIntAuto { Name = "A", Quantity = 1 });
        _connection.Insert(new ItemIntAuto { Name = "B", Quantity = 2 });
        _connection.Insert(new ItemStringKey { Id = "K-1", Name = "A", Quantity = 1 });
        _connection.Insert(new ItemStringKey { Id = "K-2", Name = "B", Quantity = 2 });
        _connection.InsertNoKeyAtt(new LogEntry { Message = "hola", Severity = 1 });
        _connection.Insert(new Order { Name = "O1" });
        _connection.Insert(new OrderLine { Id = 1, OrderId = 1, Product = "P1" });
        _connection.Insert(new OrderLine { Id = 2, OrderId = 1, Product = "P2" });
    }

    // ==== Constructor and metadata ====

    [Fact]
    public void Ctor_Extracts_Server_And_Database_Names()
    {
        Assert.Equal(_connection.Database, _db.MainDatabaseName);
        Assert.False(string.IsNullOrEmpty(_db.Server));
        Assert.True(_db.IsParentDataBase);
    }

    // ==== Insert / Update / InsertOrUpdate ====

    [Fact]
    public void Insert_Returns_New_Id()
    {
        var r = _db.Insert(new ItemIntAuto { Name = "C", Quantity = 3 });

        Assert.False(r.IsFailed);
        Assert.True(r.Value > 0);
        Assert.Equal("C", _connection.Get<ItemIntAuto>(r.Value).Name);
    }

    [Fact]
    public void Update_Modifies_Item()
    {
        var r = _db.Update(new ItemIntAuto { Id = 1, Name = "A2", Quantity = 9 });

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        var item = _connection.Get<ItemIntAuto>(1);
        Assert.Equal("A2", item.Name);
        Assert.Equal(9, item.Quantity);
    }

    [Fact]
    public void InsertOrUpdate_Inserts_Then_Updates()
    {
        var ins = _db.InsertOrUpdate(new ItemIntAuto { Name = "C", Quantity = 3 });
        Assert.False(ins.IsFailed);
        var id = ins.Value;

        var upd = _db.InsertOrUpdate(new ItemIntAuto { Id = id, Name = "C2", Quantity = 4 });

        Assert.False(upd.IsFailed);
        var item = _connection.Get<ItemIntAuto>(id);
        Assert.Equal("C2", item.Name);
    }

    // ==== Query / QueryOne / Execute / ExecuteScalar ====

    [Fact]
    public void Query_With_Param_Returns_Items()
    {
        var r = _db.Query<ItemIntAuto>("SELECT * FROM ItemsIntAuto WHERE Quantity=@q", new { q = 1 });

        Assert.False(r.IsFailed);
        var item = Assert.Single(r.Value!);
        Assert.Equal("A", item.Name);
    }

    [Fact]
    public void Query_Bad_Sql_Returns_Failed()
    {
        var r = _db.Query<ItemIntAuto>("SELECT * FROM NoExiste");

        Assert.True(r.IsFailed);
    }

    [Fact]
    public void QueryOne_Returns_Item()
    {
        var r = _db.QueryOne<ItemIntAuto>("SELECT * FROM ItemsIntAuto WHERE Id=@id", new { id = 1 });

        Assert.False(r.IsFailed);
        Assert.Equal("A", r.Value!.Name);
    }

    [Fact]
    public void Execute_Returns_Affected_Rows()
    {
        var r = _db.Execute("UPDATE ItemsIntAuto SET Quantity=Quantity+1 WHERE Id=@id", new { id = 1 });

        Assert.False(r.IsFailed);
        Assert.Equal(1, r.Value);
    }

    [Fact]
    public void ExecuteScalar_Returns_Value()
    {
        var r = _db.ExecuteScalar<int>("SELECT COUNT(*) FROM ItemsIntAuto");

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value);
    }

    // ==== Check ====

    [Fact]
    public void Check_With_Key_Table_Succeeds()
    {
        Assert.False(_db.Check<ItemIntAuto>().IsFailed);
    }

    [Fact]
    public void Check_With_NoKey_Type_Fails()
    {
        Assert.True(_db.Check<LogEntry>().IsFailed);
    }

    // ==== Get ====

    [Fact]
    public void Get_By_Int_Id_Returns_Item()
    {
        var r = _db.Get<ItemIntAuto>(1);

        Assert.False(r.IsFailed);
        Assert.Equal("A", r.Value!.Name);
    }

    [Fact]
    public void Get_By_String_Id_Returns_Item()
    {
        var r = _db.Get<ItemStringKey>("K-1");

        Assert.False(r.IsFailed);
        Assert.Equal(1, r.Value!.Quantity);
    }

    [Fact]
    public void Get_Generic_Key_Returns_Item()
    {
        var r = _db.Get<int, ItemIntAuto>(2);

        Assert.False(r.IsFailed);
        Assert.Equal("B", r.Value!.Name);
    }

    [Fact]
    public void GetSlim_By_Int_Id_Returns_Item()
    {
        var r = _db.GetSlim<ItemIntAuto>(1);

        Assert.False(r.IsFailed);
        Assert.Equal("A", r.Value!.Name);
    }

    [Fact]
    public void GetSlim_By_String_Id_Returns_Item()
    {
        var r = _db.GetSlim<ItemStringKey>("K-2");

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Quantity);
    }

    // ==== GetAll / GetAllSlim / GetAllNoKey / GetKeys ====

    [Fact]
    public void GetAll_Returns_All_Items()
    {
        var r = _db.GetAll<ItemIntAuto>();

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    [Fact]
    public void GetAllSlim_Returns_All_Items()
    {
        var r = _db.GetAllSlim<ItemIntAuto>();

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    [Fact]
    public void GetAllNoKey_Returns_Rows()
    {
        var r = _db.GetAllNoKey<LogEntry>();

        Assert.False(r.IsFailed);
        var entry = Assert.Single(r.Value!);
        Assert.Equal("hola", entry.Message);
    }

    [Fact]
    public void GetKeys_Returns_Ids()
    {
        var r = _db.GetKeys<ItemIntAuto, int>();

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    // ==== GetSome / GetSomeSlim ====

    [Fact]
    public void GetSome_Int_Ids_Skips_Missing()
    {
        var r = _db.GetSome<ItemIntAuto>(new[] { 1, 999, 2 });

        Assert.False(r.IsFailed);
        Assert.Equal(new[] { 1, 2 }, r.Value!.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void GetSome_String_Ids_Returns_Found()
    {
        var r = _db.GetSome<ItemStringKey>(new[] { "K-1", "NO", "K-2" });

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    [Fact]
    public void GetSomeSlim_Returns_Found()
    {
        var r = _db.GetSomeSlim<ItemIntAuto>(new[] { 1, 2 });

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    // ==== GetWhere / GetWhereSlim ====

    [Fact]
    public void GetWhere_Returns_Matching()
    {
        var r = _db.GetWhere<ItemIntAuto>("Id=@id", new { id = 1 });

        Assert.False(r.IsFailed);
        Assert.Equal("A", Assert.Single(r.Value!).Name);
    }

    [Fact]
    public void GetWhereSlim_Returns_Matching()
    {
        var r = _db.GetWhereSlim<ItemIntAuto>("Id=@id", new { id = 2 });

        Assert.False(r.IsFailed);
        Assert.Equal("B", Assert.Single(r.Value!).Name);
    }

    // ==== Delete ====

    [Fact]
    public void Delete_By_Item_Removes_Row()
    {
        var item = _connection.Get<ItemIntAuto>(1);
        var r = _db.Delete(item);

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Null(_connection.Get<ItemIntAuto>(1));
    }

    [Fact]
    public void Check_With_Reserved_Word_Table_Works()
    {
        // AB-4/CE-7: Check<T> generates its own SQL; with a table named
        // 'Order' (reserved) it must escape the identifier.
        TestTables.CreateReservedOrder(_connection);
        _connection.Execute("INSERT INTO [Order] ([Id], [User], [Quantity]) VALUES (1, 'A', 10)");

        var ok = _db.Check<ReservedOrderItem>();

        Assert.False(ok.IsFailed);
    }

    [Fact]
    public void Delete_By_Int_Key_Removes_Row()
    {
        var r = _db.Delete<ItemIntAuto>(1);

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Null(_connection.Get<ItemIntAuto>(1));
    }

    [Fact]
    public void Delete_By_String_Key_Removes_Row()
    {
        var r = _db.Delete<ItemStringKey>("K-1");

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Null(_connection.Get<ItemStringKey>("K-1"));
    }

    [Fact]
    public void Delete_Missing_Key_Returns_False()
    {
        var r = _db.Delete<ItemIntAuto>(999);

        Assert.False(r.IsFailed);
        Assert.False(r.Value);
    }

    // ==== UpdateIdValue ====

    [Fact]
    public void UpdateIdValue_Updates_Field()
    {
        var r = _db.UpdateIdValue<ItemIntAuto, int>(1, 42, "Quantity");

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Equal(42, _connection.Get<ItemIntAuto>(1).Quantity);
    }

    [Fact]
    public void UpdateIdValue_Tuple_Updates_Field()
    {
        var r = _db.UpdateIdValue<ItemIntAuto, int>(((object)1, 55), "Quantity");

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Equal(55, _connection.Get<ItemIntAuto>(1).Quantity);
    }

    // ==== QueryJoin ====

    [Fact]
    public void QueryJoin_Groups_Lines_By_Order()
    {
        var sql = """
            SELECT o.Id, o.Name, l.Id, l.OrderId, l.Product
            FROM Orders o JOIN OrderLines l ON o.Id = l.OrderId
            """;
        var r = _db.QueryJoin<Order, OrderLine>(sql, null, (o, l) => o.Lines.Add(l));

        Assert.False(r.IsFailed);
        var order = Assert.Single(r.Value!);
        Assert.Collection(order.Lines,
            line => Assert.Equal("P1", line.Product),
            line => Assert.Equal("P2", line.Product));
    }

    // ==== Async CRUD ====

    [Fact]
    public async Task InsertAsync_Returns_New_Id()
    {
        var r = await _db.InsertAsync(new ItemIntAuto { Name = "C", Quantity = 3 });

        Assert.False(r.IsFailed);
        Assert.True(r.Value > 0);
    }

    [Fact]
    public async Task UpdateAsync_Modifies_Item()
    {
        var r = await _db.UpdateAsync(new ItemIntAuto { Id = 1, Name = "A2", Quantity = 9 });

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Equal("A2", _connection.Get<ItemIntAuto>(1).Name);
    }

    [Fact]
    public async Task InsertOrUpdateAsync_Inserts()
    {
        var r = await _db.InsertOrUpdateAsync(new ItemIntAuto { Name = "C", Quantity = 3 });

        Assert.False(r.IsFailed);
        Assert.True(r.Value > 0);
    }

    [Fact]
    public async Task QueryAsync_Returns_Items()
    {
        var r = await _db.QueryAsync<ItemIntAuto>("SELECT * FROM ItemsIntAuto WHERE Quantity=@q", new { q = 2 });

        Assert.False(r.IsFailed);
        Assert.Equal("B", Assert.Single(r.Value!).Name);
    }

    [Fact]
    public async Task QueryOneAsync_Returns_Item()
    {
        var r = await _db.QueryOneAsync<ItemIntAuto>("SELECT * FROM ItemsIntAuto WHERE Id=@id", new { id = 1 });

        Assert.False(r.IsFailed);
        Assert.Equal("A", r.Value!.Name);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Rows()
    {
        var r = await _db.ExecuteAsync("UPDATE ItemsIntAuto SET Quantity=Quantity+1 WHERE Id=@id", new { id = 1 });

        Assert.False(r.IsFailed);
        Assert.Equal(1, r.Value);
    }

    [Fact]
    public async Task ExecuteScalarAsync_Returns_Value()
    {
        var r = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ItemsIntAuto");

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value);
    }

    [Fact]
    public async Task GetAsync_By_Int_Id_Returns_Item()
    {
        var r = await _db.GetAsync<ItemIntAuto>(1);

        Assert.False(r.IsFailed);
        Assert.Equal("A", r.Value!.Name);
    }

    [Fact]
    public async Task GetAsync_By_String_Id_Returns_Item()
    {
        var r = await _db.GetAsync<ItemStringKey>("K-2");

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Quantity);
    }

    [Fact]
    public async Task GetAllAsync_Returns_All_Items()
    {
        var r = await _db.GetAllAsync<ItemIntAuto>();

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    [Fact]
    public async Task GetAllSlimAsync_Returns_All_Items()
    {
        var r = await _db.GetAllSlimAsync<ItemIntAuto>();

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    [Fact]
    public async Task GetSomeAsync_Returns_Found()
    {
        var r = await _db.GetSomeAsync<ItemIntAuto>(new[] { 1, 999 });

        Assert.False(r.IsFailed);
        Assert.Equal(1, r.Value!.Count);
    }

    [Fact]
    public async Task GetSomeSlimAsync_Returns_Found()
    {
        var r = await _db.GetSomeSlimAsync<ItemIntAuto>(new[] { 1, 2 });

        Assert.False(r.IsFailed);
        Assert.Equal(2, r.Value!.Count);
    }

    [Fact]
    public async Task GetWhereAsync_Returns_Matching()
    {
        var r = await _db.GetWhereAsync<ItemIntAuto>("Id=@id", new { id = 1 });

        Assert.False(r.IsFailed);
        Assert.Equal("A", Assert.Single(r.Value!).Name);
    }

    [Fact]
    public async Task GetWhereSlimAsync_Returns_Matching()
    {
        var r = await _db.GetWhereSlimAsync<ItemIntAuto>("Id=@id", new { id = 2 });

        Assert.False(r.IsFailed);
        Assert.Equal("B", Assert.Single(r.Value!).Name);
    }

    [Fact]
    public async Task DeleteAsync_By_Key_Removes_Row()
    {
        var r = await _db.DeleteAsync<ItemIntAuto>(1);

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Null(_connection.Get<ItemIntAuto>(1));
    }

    [Fact]
    public async Task DeleteAsync_By_Item_Removes_Row()
    {
        var item = _connection.Get<ItemStringKey>("K-1");
        var r = await _db.DeleteAsync(item);

        Assert.False(r.IsFailed);
        Assert.True(r.Value);
        Assert.Null(_connection.Get<ItemStringKey>("K-1"));
    }
}
