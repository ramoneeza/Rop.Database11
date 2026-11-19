using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.SqlClient;
using Rop.Dapper.ContribEx11;
using Rop.Database11;
using Rop.Results9;
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.ChangeTracking.Tests;

/// <summary>
/// Tests for the Change Tracking methods of the concrete Database:
/// GetDbVersion, VerifyNotify/VerifyThrowNotify and GetTableChanges.
/// </summary>
[Collection("ChangeTracking")]
public class ChangeTrackingDatabaseTests : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlDb _db;

    public ChangeTrackingDatabaseTests(ChangeTrackingFixture fixture)
    {
        _connection = fixture.OpenConnection();
        TestTables.CreateCtItems(_connection);
        _db = new SqlDb(_connection.ConnectionString);
    }

    public void Dispose() => _connection.Dispose();

    // ==== GetDbVersion ====

    [Fact]
    public void GetDbVersion_Returns_NonNegative_Value()
    {
        // La version es global a la base de datos: puede ser > 0 si otros
        // tests de la misma base ya hicieron DML.
        var r = _db.GetDbVersion();

        Assert.False(r.IsFailed);
        Assert.True(r.Value >= 0);
    }

    [Fact]
    public void GetDbVersion_Increases_After_Dml()
    {
        var v0 = _db.GetDbVersion().ValueOrThrow();
        _connection.Insert(new CtItem { Name = "A", Quantity = 1 });
        var v1 = _db.GetDbVersion().ValueOrThrow();

        Assert.True(v1 > v0);
    }

    [Fact]
    public void GetDbVersion_Table_And_Use_Overloads_Match()
    {
        var td = _db.GetKeyDescription<CtItem>()!;
        _connection.Insert(new CtItem { Name = "A", Quantity = 1 });

        var direct = _db.GetDbVersion();
        var byTable = _db.GetDbVersion(td);
        var byUse = _db.GetDbVersion("");

        Assert.False(direct.IsFailed);
        Assert.False(byTable.IsFailed);
        Assert.False(byUse.IsFailed);
        Assert.Equal(direct.Value, byTable.Value);
        Assert.Equal(direct.Value, byUse.Value);
    }

    [Fact]
    public async Task GetDbVersionAsync_Variants_Work()
    {
        _connection.Insert(new CtItem { Name = "A", Quantity = 1 });

        var direct = await _db.GetDbVersionAsync();
        var td = _db.GetKeyDescription<CtItem>()!;
        var byTable = await _db.GetDbVersionAsync(td);
        var byUse = await _db.GetDbVersionAsync("");

        Assert.False(direct.IsFailed);
        Assert.False(byTable.IsFailed);
        Assert.False(byUse.IsFailed);
        Assert.Equal(direct.Value, byTable.Value);
        Assert.Equal(direct.Value, byUse.Value);
    }

    // ==== GetTableChanges ====

    [Fact]
    public void GetTableChanges_Reports_Insert_Update_Delete_Ordered()
    {
        // CHANGETABLE devuelve el efecto neto POR FILA: para obtener las tres
        // operaciones hay que tocar tres filas distintas.
        var b = (int)_connection.Insert(new CtItem { Name = "B", Quantity = 2 });
        var c = (int)_connection.Insert(new CtItem { Name = "C", Quantity = 3 });
        var v0 = _db.GetDbVersion().ValueOrThrow();

        var a = (int)_connection.Insert(new CtItem { Name = "A", Quantity = 1 });
        _connection.Update(new CtItem { Id = b, Name = "B2", Quantity = 22 });
        _connection.Delete(new CtItem { Id = c });

        var r = _db.GetTableChanges(v0, _db.GetKeyDescription<CtItem>()!);

        Assert.False(r.IsFailed);
        var delta = r.Value!;
        Assert.True(delta.IsNewVersion);
        Assert.False(delta.NoNewVersion);
        Assert.False(delta.IsEmpty);
        Assert.Equal(v0, delta.OldVersion);
        Assert.True(delta.Version > v0);
        Assert.Equal(3, delta.Changes.Count);
        Assert.Equal(
            new[] { ChangeOperation.Delete, ChangeOperation.Update, ChangeOperation.Insert },
            delta.Changes.Select(ch => ch.Operation));
        Assert.Equal(c, delta.Changes[0].Id);
        Assert.Equal(b, delta.Changes[1].Id);
        Assert.Equal(a, delta.Changes[2].Id);
    }

    [Fact]
    public void GetTableChanges_NoChanges_Returns_Empty()
    {
        _connection.Insert(new CtItem { Name = "A", Quantity = 1 });
        var v0 = _db.GetDbVersion().ValueOrThrow();

        var r = _db.GetTableChanges(v0, _db.GetKeyDescription<CtItem>()!);

        Assert.False(r.IsFailed);
        var delta = r.Value!;
        Assert.True(delta.IsEmpty);
        Assert.True(delta.NoNewVersion);
        Assert.False(delta.IsNewVersion);
        Assert.Equal(v0, delta.Version);
        Assert.Equal(v0, delta.OldVersion);
    }

    [Fact]
    public async Task GetTableChangesAsync_Reports_Insert()
    {
        var v0 = _db.GetDbVersion().ValueOrThrow();
        var id = (int)_connection.Insert(new CtItem { Name = "A", Quantity = 1 });

        var r = await _db.GetTableChangesAsync(v0, _db.GetKeyDescription<CtItem>()!);

        Assert.False(r.IsFailed);
        var change = Assert.Single(r.Value!.Changes);
        Assert.Equal(ChangeOperation.Insert, change.Operation);
        Assert.Equal(id, change.Id);
    }

    [Fact]
    public void GetTableChanges_PartialKey_Exposes_Second_Key()
    {
        TestTables.CreateCtPartial(_connection);
        _connection.Execute("INSERT INTO CtPartial (OwnerId, Code, Quantity) VALUES (1,'A',10),(1,'B',20)");
        var v0 = _db.GetDbVersion().ValueOrThrow();
        _connection.Execute("DELETE FROM CtPartial WHERE Code='B'");

        var td = _db.GetAnyKeyDescription<CtPartialItem>()!;
        Assert.IsType<PartialKeyDescription>(td);

        var r = _db.GetTableChanges(v0, td);

        Assert.False(r.IsFailed);
        var change = Assert.Single(r.Value!.Changes);
        Assert.Equal(1, change.Id);
        Assert.Equal("B", change.Id2);
        // SD-19: acceso tipado a las claves parciales.
        var keys = r.Value.GetPartialKeys<int, string>().ToList();
        Assert.Contains((1, "B"), keys);
    }

    // ==== VerifyNotify / VerifyThrowNotify ====

    [Fact]
    public void VerifyNotify_On_Enabled_Table_Succeeds()
    {
        Assert.False(_db.VerifyNotify<CtItem>().IsFailed);
        Assert.False(_db.VerifyNotify(_db.GetKeyDescription<CtItem>()!).IsFailed);
    }

    [Fact]
    public async Task VerifyNotifyAsync_On_Enabled_Table_Succeeds()
    {
        Assert.False((await _db.VerifyNotifyAsync<CtItem>()).IsFailed);
        Assert.False((await _db.VerifyNotifyAsync(_db.GetKeyDescription<CtItem>()!)).IsFailed);
    }

    [Fact]
    public void VerifyThrowNotify_On_Enabled_Table_Does_Not_Throw()
    {
        _db.VerifyThrowNotify<CtItem>();
        _db.VerifyThrowNotify(_db.GetKeyDescription<CtItem>()!);
    }

    [Fact]
    public void VerifyNotify_On_Table_Without_CT_Fails()
    {
        TestTables.CreateCtNoTrack(_connection);

        Assert.True(_db.VerifyNotify<CtNoTrackItem>().IsFailed);
    }

    [Fact]
    public void VerifyThrowNotify_On_Table_Without_CT_Throws()
    {
        TestTables.CreateCtNoTrack(_connection);

        Assert.ThrowsAny<Exception>(() => _db.VerifyThrowNotify<CtNoTrackItem>());
    }
}
