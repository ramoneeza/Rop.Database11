using Rop.Database;
using Rop.Results9;

namespace Rop.AbsDatabase11.Tests;

/// <summary>
/// Tests for AbsDatabase query helpers (AB-2): QueryOne and ExecuteScalar
/// must propagate the original error instead of losing it via .Value.
/// </summary>
public class AbsDatabaseTests
{
    private static FakeDatabase NewDb() => new("Data Source=FakeServer;Initial Catalog=FakeDb");

    [Fact]
    public void QueryOne_When_Connection_Fails_Returns_Failed_Result_With_Original_Error()
    {
        var db = NewDb();

        // FakeConnection.CreateDbCommand throws NotSupportedException: simulates a connection failure.
        var res = db.QueryOne<ItemIntAuto>("SELECT * FROM ItemsIntAuto");

        Assert.True(res.IsFailed);
        var error = Assert.IsType<ExceptionError>(res.Error);
        Assert.IsType<NotSupportedException>(error.Data);
    }

    [Fact]
    public void ExecuteScalar_When_Connection_Fails_Returns_Failed_Result()
    {
        var db = NewDb();

        var res = db.ExecuteScalar<int>("SELECT COUNT(*) FROM ItemsIntAuto");

        // With the broken code it returned a SUCCESSFUL Result<int> with value 0 (the error was lost).
        Assert.True(res.IsFailed);
        var error = Assert.IsType<ExceptionError>(res.Error);
        Assert.IsType<NotSupportedException>(error.Data);
    }

    // ==== AB-7: CancellationToken propagation ====

    [Fact]
    public async Task GetAllAsync_With_Cancelled_Token_Throws_OperationCanceledException()
    {
        var db = NewDb();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            db.GetAllAsync<ItemIntAuto>(cts.Token));
    }

    [Fact]
    public async Task DeleteAsync_With_Cancelled_Token_Throws_OperationCanceledException()
    {
        var db = NewDb();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            db.DeleteAsync<ItemIntAuto>(1, cts.Token));
    }
}
