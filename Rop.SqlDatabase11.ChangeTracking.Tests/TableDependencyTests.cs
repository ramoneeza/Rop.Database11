using System.Reflection;
using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.SqlClient;
using Rop.Dapper.ContribEx11;
using Rop.Database11;
using Rop.Database11.Repository;
using Rop.Database11.Tracking;
using Rop.Results9;
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.ChangeTracking.Tests;

/// <summary>
/// Tests for SqlTableDependency: registration, polling (PayLoad) and
/// the OnChanged event.
/// </summary>
[Collection("ChangeTracking")]
public class TableDependencyTests : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlDb _db;

    public TableDependencyTests(ChangeTrackingFixture fixture)
    {
        _connection = fixture.OpenConnection();
        TestTables.CreateCtItems(_connection);
        TestTables.CreateCtNoTrack(_connection);
        _db = new SqlDb(_connection.ConnectionString);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void GetTableDependency_Returns_Same_Instance_For_Same_Type()
    {
        var d1 = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime);
        var d2 = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Low);

        Assert.Same(d1, d2);
    }

    [Fact]
    public void GetTableDependency_Different_Types_Are_Different()
    {
        var d1 = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Default);
        var d2 = _db.GetTableDependency(typeof(CtNoTrackItem), ChangeTrackingPriority.Default);

        Assert.NotSame(d1, d2);
    }

    [Fact]
    public async Task PayLoad_Raises_OnChanged_With_Delta()
    {
        var dep = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime);
        var tcs = new TaskCompletionSource<DeltaChanges>(TaskCreationOptions.RunContinuationsAsynchronously);
        dep.OnChanged += (_, delta) => tcs.TrySetResult(delta);

        var id = (int)_connection.Insert(new CtItem { Name = "A", Quantity = 1 });

        // PayLoad must not return an error (it returns the exception message on failure).
        Assert.False(dep.PayLoad().IsFailed);

        var delta = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var change = Assert.Single(delta.Changes);
        Assert.Equal(ChangeOperation.Insert, change.Operation);
        Assert.Equal(id, change.Id);
        Assert.Equal(id, delta.GetKeys<int>().Single()); // SD-19: typed keys
        Assert.True(delta.IsNewVersion);
        Assert.Equal(delta.Version, dep.TableVersion.Version);
    }

    [Fact]
    public void PayLoad_Returns_Failed_Result_On_Table_Without_CT()
    {
        // CH-1: the operational error travels as a failed VoidResult (Error),
        // not as a string.
        var dep = _db.GetTableDependency(typeof(CtNoTrackItem), ChangeTrackingPriority.Default);

        var error = dep.PayLoad();

        Assert.True(error.IsFailed);
        Assert.Contains("change tracking", error.Error!.Value!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CalcKey_Uses_Database_And_Table_Name()
    {
        Assert.Equal($"{_db.MainDatabaseName}.CtItems", SqlTableDependency.CalcKey(_db, typeof(CtItem)));
    }

    [Fact]
    public async Task OnChanged_Delivers_Deltas_In_Order()
    {
        // SD-8: deltas are delivered in order and serialized (FIFO channel with
        // a single reader). Previously each delta ran in its own Task.Run.
        var dep = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime);
        var received = new List<DeltaChanges>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dep.OnChanged += (_, d) =>
        {
            lock (received)
            {
                received.Add(d);
                if (received.Count == 2) tcs.TrySetResult();
            }
        };

        var id = (int)_connection.Insert(new CtItem { Name = "A", Quantity = 1 });
        Assert.False(dep.PayLoad().IsFailed);
        _connection.Update(new CtItem { Id = id, Name = "A2", Quantity = 2 });
        Assert.False(dep.PayLoad().IsFailed);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        lock (received)
        {
            Assert.Equal(2, received.Count);
            Assert.Equal(ChangeOperation.Insert, received[0].Changes[0].Operation);
            Assert.Equal(ChangeOperation.Update, received[1].Changes[0].Operation);
            Assert.True(received[1].Version > received[0].Version);
        }
    }

    [Fact]
    public async Task OnChanged_Handler_Exception_Does_Not_Break_Delivery()
    {
        // SD-8: a handler that throws does not prevent the other handlers of
        // the same invocation from receiving the delta, nor the next delta
        // from being delivered.
        var dep = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime);
        var secondCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dep.OnChanged += (_, _) => throw new InvalidOperationException("handler roto");
        dep.OnChanged += (_, _) => secondCalled.TrySetResult();

        var id = (int)_connection.Insert(new CtItem { Name = "A", Quantity = 1 });
        Assert.False(dep.PayLoad().IsFailed);
        await secondCalled.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // The reader is still alive: a second delta reaches a third handler.
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dep.OnChanged += (_, _) => third.TrySetResult();
        _connection.Update(new CtItem { Id = id, Name = "A2", Quantity = 2 });
        Assert.False(dep.PayLoad().IsFailed);
        await third.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void PayLoad_With_Cancelled_Token_Throws_OperationCanceledException()
    {
        // GE-4: cooperative cancellation propagates (the CycleTaskService
        // uses it to stop cleanly).
        var dep = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime);

        Assert.ThrowsAny<OperationCanceledException>(() => dep.PayLoad(new CancellationToken(true)));
    }

    [Fact]
    public async Task PayLoad_Works_With_Reserved_Word_Table()
    {
        // CE-7: 'changetable(changes [Order], ...)' with escaped identifiers
        // (table with a reserved word).
        TestTables.CreateCtOrder(_connection);
        var dep = _db.GetTableDependency(typeof(CtOrderItem), ChangeTrackingPriority.Realtime);
        var tcs = new TaskCompletionSource<DeltaChanges>(TaskCreationOptions.RunContinuationsAsynchronously);
        dep.OnChanged += (_, delta) => tcs.TrySetResult(delta);

        _connection.Execute("INSERT INTO [Order] ([Id], [User], [Quantity]) VALUES (1, 'A', 10)");

        Assert.False(dep.PayLoad().IsFailed);
        var delta = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var change = Assert.Single(delta.Changes);
        Assert.Equal(ChangeOperation.Insert, change.Operation);
    }

    [Fact]
    public async Task Dispose_Stops_Delivery_And_PayLoad_Returns_Error()
    {
        // SD-17: disposing the dependency completes the channel, no more
        // deltas are delivered, and PayLoad returns an error.
        var dep = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime);
        var received = 0;
        dep.OnChanged += (_, _) => Interlocked.Increment(ref received);

        dep.Dispose();
        dep.Dispose(); // idempotent

        _connection.Insert(new CtItem { Name = "A", Quantity = 1 });

        var error = dep.PayLoad();
        Assert.True(error.IsFailed);
        Assert.Contains("disposed", error.Error!.Value!);

        await Task.Delay(300);
        Assert.Equal(0, Volatile.Read(ref received));
    }

    [Fact]
    public void Database_Dispose_Is_Idempotent_And_External_Dispose_Is_NoOp()
    {
        // SD-17: only the root Database disposes the cycle service; the child
        // databases (shared and cached) are no-ops.
        var db = new SqlDb(_connection.ConnectionString);
        db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime); // starts the loop

        var ext = db.FactoryExternalDatabase("master");
        ext.Dispose();
        Assert.NotNull(ext.FactoryConnection()); // the child database is still usable

        db.Dispose();
        db.Dispose(); // idempotent
    }

    [Fact]
    public async Task GetDbVersionAsync_With_Cancelled_Token_Throws()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _db.GetDbVersionAsync(new CancellationToken(true)));
    }

    [Fact]
    public async Task GetTableChangesAsync_With_Cancelled_Token_Throws()
    {
        var td = DapperHelperExtend.GetAnyKeyDescription(typeof(CtItem))!;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _db.GetTableChangesAsync(0, td, new CancellationToken(true)));
    }

    [Fact]
    public void Repository_Dispose_Unsubscribes_From_TableDependency()
    {
        // SD-17: disposing the repository unsubscribes it from change
        // tracking; the shared dependency and database stay alive.
        var repo = new CtRepo(_db);
        var dep = repo.TableDependency;
        Assert.Equal(1, HandlerCount(dep));

        repo.Dispose();

        Assert.Equal(0, HandlerCount(dep));
        Assert.False(dep.PayLoad().IsFailed); // the dependency is still operational
    }

    private static int HandlerCount(SqlTableDependency dep)
    {
        // The backing field of a field-like event is private.
        var field = typeof(SqlTableDependency).GetField("OnChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var del = (Delegate?)field.GetValue(dep);
        return del?.GetInvocationList().Length ?? 0;
    }

    private sealed class CtRepo : AbsSqlRepositoryK<CtItem, CtItem, int>
    {
        public CtRepo(SqlDb db) : base(db, ChangeTrackingPriority.Realtime)
        {
        }

        protected override CtItem Map(CtItem item) => item;
    }
}
