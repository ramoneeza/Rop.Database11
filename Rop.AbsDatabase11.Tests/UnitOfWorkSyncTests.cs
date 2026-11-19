using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Rop.Database;
using Rop.Results9;

namespace Rop.AbsDatabase11.Tests;

/// <summary>
/// Fake DbTransaction that records Commit/Rollback calls.
/// </summary>
internal sealed class FakeTransaction : DbTransaction
{
    private readonly FakeConnection _connection;
    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }

    public FakeTransaction(FakeConnection connection) => _connection = connection;

    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    protected override DbConnection DbConnection => _connection;

    public override void Commit()
    {
        if (RolledBack) throw new InvalidOperationException("Transaction already rolled back");
        Committed = true;
    }

    public override void Rollback()
    {
        if (Committed) throw new InvalidOperationException("Transaction already committed");
        RolledBack = true;
    }

    public override Task CommitAsync(CancellationToken cancellationToken) { Commit(); return Task.CompletedTask; }
    public override Task RollbackAsync(CancellationToken cancellationToken) { Rollback(); return Task.CompletedTask; }
}

/// <summary>
/// Fake DbConnection that records Open/Close and created transactions.
/// </summary>
internal sealed class FakeConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;
    public List<FakeTransaction> Transactions { get; } = [];
    public int OpenCount { get; private set; }

    [AllowNull]
    public override string ConnectionString { get; set; } = "";
    public override string Database => "FakeDb";
    public override string DataSource => "FakeServer";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => _state;

    public override void Open()
    {
        if (_state == ConnectionState.Open) throw new InvalidOperationException("Already open");
        _state = ConnectionState.Open;
        OpenCount++;
    }

    public override void Close() => _state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Open();
        return Task.CompletedTask;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        var tr = new FakeTransaction(this);
        Transactions.Add(tr);
        return tr;
    }

    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
}

/// <summary>
/// Minimal AbsDatabase whose factory returns a FakeConnection (or throws, to test factory failures).
/// </summary>
internal sealed class FakeDatabase : AbsDatabase
{
    private readonly Func<DbConnection>? _factory;
    public FakeConnection? LastConnection { get; private set; }
    public FakeTransaction? LastTransaction => LastConnection?.Transactions.LastOrDefault();

    public FakeDatabase(string connectionString, Func<DbConnection>? factory = null) : base(connectionString)
    {
        _factory = factory;
    }

    public override DbConnection FactoryConnection()
    {
        var conn = _factory is not null ? _factory() : new FakeConnection();
        LastConnection = (FakeConnection)conn;
        return LastConnection;
    }
}

/// <summary>
/// Tests for the 8 synchronous UnitOfWork combinations:
/// { Conn | Conn+Tr } × { Result<T> | EnumerableResult<T> | IEnumerable<T> | T }.
/// </summary>
public class UnitOfWorkSyncTests
{
    private static FakeDatabase NewDb() => new("Data Source=FakeServer;Initial Catalog=FakeDb");

    private static Result<T> Ok<T>(T value) => new(value);
    private static Result<T> Failed<T>(string message) => Error.Exception(new Exception(message));
    private static EnumerableResult<T> OkEnumerable<T>(IEnumerable<T> values) => new(values);
    private static EnumerableResult<T> FailedEnumerable<T>(string message) => Error.Exception(new Exception(message));
    private static int Boom() => throw new InvalidOperationException("boom");
    private static IEnumerable<int> Throwing() => throw new InvalidOperationException("boom");

    // ==== (Conn, Tr) -> Result<T> ====

    [Fact]
    public void WithTr_Result_Success_Commits_And_Returns_Value()
    {
        var db = NewDb();
        var res = db.UnitOfWorkSingle((conn, tr) => Ok(42));

        Assert.False(res.IsFailed);
        Assert.Equal(42, res.Value);
        Assert.True(db.LastTransaction!.Committed);
        Assert.False(db.LastTransaction.RolledBack);
        Assert.Equal(ConnectionState.Closed, db.LastConnection!.State);
    }

    [Fact]
    public void WithTr_Result_Failed_RollsBack_And_Returns_Error()
    {
        var db = NewDb();
        var res = db.UnitOfWorkSingle<int>((conn, tr) => Failed<int>("negocio"));

        Assert.True(res.IsFailed);
        Assert.True(db.LastTransaction!.RolledBack);
        Assert.False(db.LastTransaction.Committed);
    }

    // ==== (Conn, Tr) -> EnumerableResult<T> ====

    [Fact]
    public void WithTr_EnumerableResult_Success_Commits()
    {
        var db = NewDb();
        var res = db.UnitOfWork((conn, tr) => OkEnumerable(new[] { 1, 2, 3 }));

        Assert.False(res.IsFailed);
        Assert.Equal(new[] { 1, 2, 3 }, res.Value);
        Assert.True(db.LastTransaction!.Committed);
    }

    [Fact]
    public void WithTr_EnumerableResult_Failed_RollsBack()
    {
        var db = NewDb();
        var res = db.UnitOfWork<int>((conn, tr) => FailedEnumerable<int>("negocio"));

        Assert.True(res.IsFailed);
        Assert.True(db.LastTransaction!.RolledBack);
        Assert.False(db.LastTransaction.Committed);
    }

    // ==== (Conn, Tr) -> IEnumerable<T> ====

    [Fact]
    public void WithTr_IEnumerable_Success_Commits()
    {
        var db = NewDb();
        var res = db.UnitOfWork((conn, tr) => (IEnumerable<int>)[1, 2]);

        Assert.False(res.IsFailed);
        Assert.Equal(new[] { 1, 2 }, res.Value);
        Assert.True(db.LastTransaction!.Committed);
    }

    [Fact]
    public void WithTr_IEnumerable_Throwing_RollsBack_And_Returns_Exception()
    {
        var db = NewDb();
        var res = db.UnitOfWork<int>((conn, tr) => Throwing());

        Assert.True(res.IsFailed);
        Assert.True(db.LastTransaction!.RolledBack);
        Assert.False(db.LastTransaction.Committed);
    }

    // ==== (Conn, Tr) -> T ====

    [Fact]
    public void WithTr_Value_Success_Commits()
    {
        var db = NewDb();
        var res = db.UnitOfWorkSingle((conn, tr) => 7);

        Assert.False(res.IsFailed);
        Assert.Equal(7, res.Value);
        Assert.True(db.LastTransaction!.Committed);
    }

    [Fact]
    public void WithTr_Value_Throwing_RollsBack_And_Returns_Exception()
    {
        var db = NewDb();
        var res = db.UnitOfWorkSingle<int>((conn, tr) => Boom());

        Assert.True(res.IsFailed);
        Assert.True(db.LastTransaction!.RolledBack);
    }

    // ==== Conn -> Result<T> ====

    [Fact]
    public void NoTr_Result_Success_Returns_Without_Transaction()
    {
        var db = NewDb();
        var res = db.UnitOfWorkSingle((conn) => Ok("hola"));

        Assert.False(res.IsFailed);
        Assert.Equal("hola", res.Value);
        Assert.Empty(db.LastConnection!.Transactions);
        Assert.Equal(ConnectionState.Closed, db.LastConnection.State);
    }

    [Fact]
    public void NoTr_Result_Failed_Returns_Error()
    {
        var db = NewDb();
        var res = db.UnitOfWorkSingle<string>((conn) => Failed<string>("negocio"));

        Assert.True(res.IsFailed);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    // ==== Conn -> EnumerableResult<T> ====

    [Fact]
    public void NoTr_EnumerableResult_Success_Returns()
    {
        var db = NewDb();
        var res = db.UnitOfWork((conn) => OkEnumerable(new[] { 1, 2 }));

        Assert.False(res.IsFailed);
        Assert.Equal(new[] { 1, 2 }, res.Value);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    // ==== Conn -> IEnumerable<T> ====

    [Fact]
    public void NoTr_IEnumerable_Throwing_Returns_Exception()
    {
        var db = NewDb();
        var res = db.UnitOfWork<int>((conn) => Throwing());

        Assert.True(res.IsFailed);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    // ==== Conn -> T? ====

    [Fact]
    public void NoTr_Value_Success_Returns_Value()
    {
        var db = NewDb();
        var res = db.UnitOfWorkSingle((conn) => (int?)9);

        Assert.False(res.IsFailed);
        Assert.Equal(9, res.Value);
    }

    [Fact]
    public void NoTr_Value_Throwing_Returns_Exception()
    {
        var db = NewDb();
        var res = db.UnitOfWorkSingle<int>((conn) => (int?)Boom());

        Assert.True(res.IsFailed);
        Assert.Empty(db.LastConnection!.Transactions);
    }

    // ==== Semántica adicional ====

    [Fact]
    public void FactoryConnection_Throwing_Returns_Error_Instead_Of_Throwing()
    {
        var db = new FakeDatabase(
            "Data Source=FakeServer;Initial Catalog=FakeDb",
            () => throw new InvalidOperationException("factory"));

        var withTr = db.UnitOfWorkSingle((conn, tr) => Ok(1));
        var noTr = db.UnitOfWorkSingle((conn) => Ok(1));

        Assert.True(withTr.IsFailed);
        Assert.True(noTr.IsFailed);
    }
}
