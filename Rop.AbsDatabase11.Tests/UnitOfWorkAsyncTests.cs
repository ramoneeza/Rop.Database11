using System.Data;
using System.Data.Common;
using Rop.Results9;

namespace Rop.AbsDatabase11.Tests;

/// <summary>
/// Tests for the 16 asynchronous UnitOfWork combinations:
/// { Conn | Conn+Tr } × { Result<T> | EnumerableResult<T> | IEnumerable<T> | T } × { no ct | with ct }.
/// Explicit parameter types are used in the lambdas to avoid CS0121 ambiguity
/// between the DbTransaction and CancellationToken overloads.
/// </summary>
public class UnitOfWorkAsyncTests
{
    private static FakeDatabase NewDb() => new("Data Source=FakeServer;Initial Catalog=FakeDb");

    private static Task<Result<T>> OkAsync<T>(T value) => Task.FromResult<Result<T>>(new(value));
    private static Task<Result<T>> FailedAsync<T>(string message) => Task.FromResult<Result<T>>(Error.Exception(new Exception(message)));
    private static Task<EnumerableResult<T>> OkEnumerableAsync<T>(IEnumerable<T> values) => Task.FromResult<EnumerableResult<T>>(new(values));
    private static Task<T> ValueAsync<T>(T value) => Task.FromResult(value);
    private static Task<IEnumerable<T>> SeqAsync<T>(IEnumerable<T> values) => Task.FromResult(values);

    // ==== (Conn, Tr) -> Result<T> ====

    [Fact]
    public async Task WithTr_Result_NoCt_Success_Commits()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkSingleAsync((DbConnection conn, DbTransaction tr) => OkAsync(42));

        Assert.False(res.IsFailed);
        Assert.Equal(42, res.Value);
        Assert.True(db.LastTransaction!.Committed);
    }

    [Fact]
    public async Task WithTr_Result_Ct_Success_Commits_And_Forwards_Token()
    {
        var db = NewDb();
        var cts = new CancellationTokenSource();
        var seen = CancellationToken.None;
        var res = await db.UnitOfWorkSingleAsync((DbConnection conn, DbTransaction tr, CancellationToken c) =>
        {
            seen = c;
            return OkAsync(42);
        }, cts.Token);

        Assert.False(res.IsFailed);
        Assert.True(db.LastTransaction!.Committed);
        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public async Task WithTr_Result_Failed_RollsBack()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkSingleAsync<int>((DbConnection conn, DbTransaction tr) => FailedAsync<int>("negocio"));

        Assert.True(res.IsFailed);
        Assert.True(db.LastTransaction!.RolledBack);
    }

    // ==== (Conn, Tr) -> EnumerableResult<T> ====

    [Fact]
    public async Task WithTr_EnumerableResult_NoCt_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkAsync<int>((DbConnection conn, DbTransaction tr) => OkEnumerableAsync(new[] { 1, 2 }));

        Assert.False(res.IsFailed);
        Assert.Equal(new[] { 1, 2 }, res.Value);
        Assert.True(db.LastTransaction!.Committed);
    }

    [Fact]
    public async Task WithTr_EnumerableResult_Ct_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkAsync<int>((DbConnection conn, DbTransaction tr, CancellationToken c) => OkEnumerableAsync(new[] { 1, 2 }));

        Assert.False(res.IsFailed);
        Assert.True(db.LastTransaction!.Committed);
    }

    // ==== (Conn, Tr) -> IEnumerable<T> ====

    [Fact]
    public async Task WithTr_IEnumerable_NoCt_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkAsync<int>((DbConnection conn, DbTransaction tr) => SeqAsync(new[] { 1, 2 }));

        Assert.False(res.IsFailed);
        Assert.Equal(new[] { 1, 2 }, res.Value);
        Assert.True(db.LastTransaction!.Committed);
    }

    [Fact]
    public async Task WithTr_IEnumerable_Ct_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkAsync<int>((DbConnection conn, DbTransaction tr, CancellationToken c) => SeqAsync(new[] { 1, 2 }));

        Assert.False(res.IsFailed);
        Assert.True(db.LastTransaction!.Committed);
    }

    // ==== (Conn, Tr) -> T ====

    [Fact]
    public async Task WithTr_Value_NoCt_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkSingleAsync<int>((DbConnection conn, DbTransaction tr) => ValueAsync(7));

        Assert.False(res.IsFailed);
        Assert.Equal(7, res.Value);
        Assert.True(db.LastTransaction!.Committed);
    }

    [Fact]
    public async Task WithTr_Value_Ct_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkSingleAsync<int>((DbConnection conn, DbTransaction tr, CancellationToken c) => ValueAsync(7));

        Assert.False(res.IsFailed);
        Assert.True(db.LastTransaction!.Committed);
    }

    // ==== Conn -> Result<T> ====

    [Fact]
    public async Task NoTr_Result_NoCt_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkSingleAsync<int>((DbConnection conn) => OkAsync(42));

        Assert.False(res.IsFailed);
        Assert.Equal(42, res.Value);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    [Fact]
    public async Task NoTr_Result_Ct_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkSingleAsync<int>((DbConnection conn, CancellationToken c) => OkAsync(42));

        Assert.False(res.IsFailed);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    // ==== Conn -> EnumerableResult<T> ====

    [Fact]
    public async Task NoTr_EnumerableResult_NoCt_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkAsync<int>((DbConnection conn) => OkEnumerableAsync(new[] { 1, 2 }));

        Assert.False(res.IsFailed);
        Assert.Equal(new[] { 1, 2 }, res.Value);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    [Fact]
    public async Task NoTr_EnumerableResult_Ct_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkAsync<int>((DbConnection conn, CancellationToken c) => OkEnumerableAsync(new[] { 1, 2 }));

        Assert.False(res.IsFailed);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    // ==== Conn -> IEnumerable<T> ====

    [Fact]
    public async Task NoTr_IEnumerable_NoCt_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkAsync<int>((DbConnection conn) => SeqAsync(new[] { 1, 2 }));

        Assert.False(res.IsFailed);
        Assert.Equal(new[] { 1, 2 }, res.Value);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    [Fact]
    public async Task NoTr_IEnumerable_Ct_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkAsync<int>((DbConnection conn, CancellationToken c) => SeqAsync(new[] { 1, 2 }));

        Assert.False(res.IsFailed);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    // ==== Conn -> T? ====

    [Fact]
    public async Task NoTr_Value_NoCt_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkSingleAsync<int>((DbConnection conn) => ValueAsync(9));

        Assert.False(res.IsFailed);
        Assert.Equal(9, res.Value);
    }

    [Fact]
    public async Task NoTr_Value_Ct_Success()
    {
        var db = NewDb();
        var res = await db.UnitOfWorkSingleAsync<int>((DbConnection conn, CancellationToken c) => ValueAsync(9));

        Assert.False(res.IsFailed);
        Assert.Equal(9, res.Value);
    }

    // ==== Cancellation ====

    [Fact]
    public async Task Cancelled_Token_Throws_OperationCanceledException_And_RollsBack()
    {
        var db = NewDb();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            db.UnitOfWorkSingleAsync<int>((DbConnection conn, DbTransaction tr) => OkAsync(1), cts.Token));
    }
}
