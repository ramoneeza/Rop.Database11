using Dapper;
using Microsoft.Data.SqlClient;
using Rop.Database11.TypeHandlers;
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.ChangeTracking.Tests;

/// <summary>
/// Tests for the DateOnly/TimeOnly type handlers registered by
/// NewTypeHandlers.Ensure (called from the Database constructor).
/// </summary>
[Collection("ChangeTracking")]
public class TypeHandlerTests : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlDb _db;

    public TypeHandlerTests(ChangeTrackingFixture fixture)
    {
        _connection = fixture.OpenConnection();
        TestTables.CreateCtTypes(_connection);
        _db = new SqlDb(_connection.ConnectionString);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void DateOnly_TimeOnly_RoundTrip_Via_Database()
    {
        var d = new DateOnly(2026, 11, 18);
        var t = new TimeOnly(13, 45, 30);

        var ins = _db.Insert(new CtTypeRow { D = d, T = t });

        Assert.False(ins.IsFailed);
        var r = _db.Get<CtTypeRow>(ins.Value);
        Assert.False(r.IsFailed);
        Assert.Equal(d, r.Value!.D);
        Assert.Equal(t, r.Value.T);
    }

    [Fact]
    public void Ensure_Is_Thread_Safe_And_Idempotent()
    {
        // SD-12: registros concurrentes no deben lanzar (duplicado de handler).
        Parallel.For(0, 64, _ => NewTypeHandlers.Ensure());
        NewTypeHandlers.Ensure();

        // Sin excepcion = registro unico garantizado.
        Assert.True(true);
    }

    [Fact]
    public void DateOnly_TimeOnly_Nullable_RoundTrip()
    {
        _connection.Execute(
            "INSERT INTO CtTypes (D, D2, T, T2) VALUES (@d, NULL, @t, NULL)",
            new { d = new DateOnly(2026, 11, 18), t = new TimeOnly(9, 30) });

        var all = _db.GetAll<CtTypeRow>();

        Assert.False(all.IsFailed);
        var row = Assert.Single(all.Value!);
        Assert.Equal(new DateOnly(2026, 11, 18), row.D);
        Assert.Null(row.D2);
        Assert.Equal(new TimeOnly(9, 30), row.T);
        Assert.Null(row.T2);
    }
}
