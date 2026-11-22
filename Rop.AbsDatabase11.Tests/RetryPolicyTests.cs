using Rop.Database;
using Rop.Results9;

namespace Rop.AbsDatabase11.Tests;

/// <summary>
/// "Transient" exception for the retry tests (real SqlExceptions
/// cannot be constructed; the policy predicate is configured in the test).
/// </summary>
internal sealed class TestTransientException : Exception
{
    public TestTransientException() : base("transient") { }
}

/// <summary>
/// Tests for the retry policy (AB-8): retry on transient errors
/// on the transactional and non-transactional paths, sync and async.
/// </summary>
public class RetryPolicyTests
{
    private static RetryPolicy FastPolicy(int retries) => new()
    {
        MaxRetries = retries,
        Delays = Enumerable.Repeat(TimeSpan.Zero, Math.Max(retries, 1)).ToArray(),
        IsTransient = ex => ex is TestTransientException
    };

    private static FakeDatabase NewDb(Action? onFactory = null) =>
        new("Data Source=FakeServer;Initial Catalog=FakeDb",
            () => { onFactory?.Invoke(); return new FakeConnection(); });

    private static Result<T> Failed<T>(string message) => Error.Exception(new Exception(message));

    // ==== Sync WithTr ====

    [Fact]
    public void WithTr_Retries_Transient_Error_And_Succeeds_With_New_Connections()
    {
        var calls = 0;
        var connections = 0;
        var db = NewDb(() => connections++);
        db.RetryPolicy = FastPolicy(3);

        var res = db.UnitOfWorkSingle<int>((conn, tr) =>
        {
            calls++;
            if (calls == 1) throw new TestTransientException();
            return 42;
        });

        Assert.False(res.IsFailed);
        Assert.Equal(42, res.Value);
        Assert.Equal(2, calls);
        Assert.Equal(2, connections); // a new connection per attempt
        Assert.True(db.LastTransaction!.Committed);
    }

    [Fact]
    public void WithTr_Gives_Up_After_MaxRetries_And_Returns_Last_Error()
    {
        var calls = 0;
        var db = NewDb();
        db.RetryPolicy = FastPolicy(2);

        var res = db.UnitOfWorkSingle<int>((conn, tr) =>
        {
            calls++;
            throw new TestTransientException();
        });

        Assert.True(res.IsFailed);
        Assert.Equal(3, calls); // 1 attempt + 2 retries
        Assert.IsType<TestTransientException>(((ExceptionError)res.Error!).Data);
    }

    [Fact]
    public void WithTr_NonTransient_Error_Is_Not_Retried()
    {
        var calls = 0;
        var db = NewDb();
        db.RetryPolicy = FastPolicy(3);

        var res = db.UnitOfWorkSingle<int>((conn, tr) =>
        {
            calls++;
            throw new InvalidOperationException("no reintentar");
        });

        Assert.True(res.IsFailed);
        Assert.Equal(1, calls);
        Assert.True(db.LastTransaction!.RolledBack);
    }

    [Fact]
    public void WithTr_Business_Failure_Is_Not_Retried()
    {
        var calls = 0;
        var db = NewDb();
        db.RetryPolicy = FastPolicy(3);

        var res = db.UnitOfWorkSingle<int>((conn, tr) =>
        {
            calls++;
            return Failed<int>("negocio");
        });

        Assert.True(res.IsFailed);
        Assert.Equal(1, calls);
        Assert.True(db.LastTransaction!.RolledBack);
    }

    [Fact]
    public void WithTr_First_Attempt_RollsBack_Silently_Before_Retry()
    {
        var connections = new List<FakeConnection>();
        var db = new FakeDatabase("Data Source=FakeServer;Initial Catalog=FakeDb", () =>
        {
            var c = new FakeConnection();
            connections.Add(c);
            return c;
        });
        db.RetryPolicy = FastPolicy(1);
        var calls = 0;

        var res = db.UnitOfWorkSingle<int>((conn, tr) =>
        {
            calls++;
            if (calls == 1) throw new TestTransientException();
            return 7;
        });

        Assert.False(res.IsFailed);
        Assert.Equal(2, calls);
        Assert.Equal(2, connections.Count);
        // the first attempt rolled back (silently); the second committed
        Assert.True(connections[0].Transactions[0].RolledBack);
        Assert.False(connections[0].Transactions[0].Committed);
        Assert.True(connections[1].Transactions[0].Committed);
    }

    // ==== Sync NoTr ====

    [Fact]
    public void NoTr_Retries_Transient_Error_And_Succeeds()
    {
        var calls = 0;
        var connections = 0;
        var db = NewDb(() => connections++);
        db.RetryPolicy = FastPolicy(3);

        var res = db.UnitOfWorkSingle<int>(conn =>
        {
            calls++;
            if (calls < 3) throw new TestTransientException();
            return 5;
        });

        Assert.False(res.IsFailed);
        Assert.Equal(5, res.Value);
        Assert.Equal(3, calls);
        Assert.Equal(3, connections);
    }

    [Fact]
    public void NoTr_Gives_Up_After_MaxRetries()
    {
        var calls = 0;
        var db = NewDb();
        db.RetryPolicy = FastPolicy(1);

        var res = db.UnitOfWorkSingle<int>(conn =>
        {
            calls++;
            throw new TestTransientException();
        });

        Assert.True(res.IsFailed);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void NoRetry_Policy_Disables_Retries()
    {
        var calls = 0;
        var db = NewDb();
        db.RetryPolicy = RetryPolicy.NoRetry;

        var res = db.UnitOfWorkSingle<int>(conn =>
        {
            calls++;
            throw new TestTransientException();
        });

        Assert.True(res.IsFailed);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FactoryConnection_Failure_Is_Retried_Too()
    {
        var calls = 0;
        var db = new FakeDatabase("Data Source=FakeServer;Initial Catalog=FakeDb", () =>
        {
            calls++;
            if (calls == 1) throw new TestTransientException();
            return new FakeConnection();
        });
        db.RetryPolicy = FastPolicy(3);

        var res = db.UnitOfWorkSingle<int>((conn, tr) => 1);

        Assert.False(res.IsFailed);
        Assert.Equal(2, calls);
    }

    // ==== Async ====

    [Fact]
    public async Task Async_WithTr_Retries_Transient_Error_And_Succeeds()
    {
        var calls = 0;
        var connections = 0;
        var db = NewDb(() => connections++);
        db.RetryPolicy = FastPolicy(3);

        var res = await db.UnitOfWorkSingleAsync<int>(async (conn, tr, ct) =>
        {
            calls++;
            if (calls == 1) throw new TestTransientException();
            return 42;
        }, CancellationToken.None);

        Assert.False(res.IsFailed);
        Assert.Equal(42, res.Value);
        Assert.Equal(2, calls);
        Assert.Equal(2, connections);
    }

    [Fact]
    public async Task Async_Gives_Up_After_MaxRetries()
    {
        var calls = 0;
        var db = NewDb();
        db.RetryPolicy = FastPolicy(2);

        var res = await db.UnitOfWorkSingleAsync<int>(async (conn, tr, ct) =>
        {
            calls++;
            throw new TestTransientException();
        }, CancellationToken.None);

        Assert.True(res.IsFailed);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Async_Cancellation_Has_Priority_Over_Retry_Delay()
    {
        var db = NewDb();
        db.RetryPolicy = new RetryPolicy
        {
            MaxRetries = 5,
            Delays = [TimeSpan.FromSeconds(10)],
            IsTransient = _ => true
        };
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await db.UnitOfWorkSingleAsync<int>(async (conn, tr, ct) =>
            {
                throw new TestTransientException();
            }, cts.Token);
        });
    }

    // ==== RetryPolicy (detection and delays) ====

    [Fact]
    public void IsTransientDefault_Classifies_Common_Cases()
    {
        Assert.True(RetryPolicy.IsTransientDefault(new TimeoutException()));
        Assert.True(RetryPolicy.IsTransientDefault(new AggregateException(new TimeoutException())));
        Assert.False(RetryPolicy.IsTransientDefault(new InvalidOperationException()));
        Assert.False(RetryPolicy.IsTransientDefault(new OperationCanceledException()));
        Assert.False(RetryPolicy.IsTransientDefault(new TestTransientException()));
    }

    [Fact]
    public void Policy_Defaults_Are_The_Documented_Ones()
    {
        Assert.Equal(3, RetryPolicy.Default.MaxRetries);
        Assert.Equal(3, RetryPolicy.Default.Delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(100), RetryPolicy.Default.Delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(250), RetryPolicy.Default.Delays[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(500), RetryPolicy.Default.Delays[2]);
        Assert.Equal(0, RetryPolicy.NoRetry.MaxRetries);
    }
}
