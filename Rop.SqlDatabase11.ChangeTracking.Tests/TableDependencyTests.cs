using System.Reflection;
using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.SqlClient;
using Rop.Dapper.ContribEx11;
using Rop.Database11;
using Rop.Database11.Repository;
using Rop.Database11.Tracking;
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

        // PayLoad no debe devolver error (devuelve el mensaje de excepcion si falla).
        Assert.Null(dep.PayLoad());

        var delta = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var change = Assert.Single(delta.Changes);
        Assert.Equal(ChangeOperation.Insert, change.Operation);
        Assert.Equal(id, change.Id);
        Assert.Equal(id, delta.GetKeys<int>().Single()); // SD-19: claves tipadas
        Assert.True(delta.IsNewVersion);
        Assert.Equal(delta.Version, dep.TableVersion.Version);
    }

    [Fact]
    public void PayLoad_Returns_Error_Message_On_Table_Without_CT()
    {
        var dep = _db.GetTableDependency(typeof(CtNoTrackItem), ChangeTrackingPriority.Default);

        var error = dep.PayLoad();

        Assert.NotNull(error);
        Assert.Contains("change tracking", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CalcKey_Uses_Database_And_Table_Name()
    {
        Assert.Equal($"{_db.MainDatabaseName}.CtItems", SqlTableDependency.CalcKey(_db, typeof(CtItem)));
    }

    [Fact]
    public async Task OnChanged_Delivers_Deltas_In_Order()
    {
        // SD-8: los deltas se entregan en orden y serializados (canal FIFO con
        // un unico lector). Antes cada delta iba en su propio Task.Run.
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
        Assert.Null(dep.PayLoad());
        _connection.Update(new CtItem { Id = id, Name = "A2", Quantity = 2 });
        Assert.Null(dep.PayLoad());

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
        // SD-8: un handler que lanza no impide que los demas handlers de la misma
        // invocacion reciban el delta, ni que el siguiente delta se entregue.
        var dep = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime);
        var secondCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dep.OnChanged += (_, _) => throw new InvalidOperationException("handler roto");
        dep.OnChanged += (_, _) => secondCalled.TrySetResult();

        var id = (int)_connection.Insert(new CtItem { Name = "A", Quantity = 1 });
        Assert.Null(dep.PayLoad());
        await secondCalled.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // El lector sigue vivo: un segundo delta llega a un tercer handler.
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dep.OnChanged += (_, _) => third.TrySetResult();
        _connection.Update(new CtItem { Id = id, Name = "A2", Quantity = 2 });
        Assert.Null(dep.PayLoad());
        await third.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void PayLoad_With_Cancelled_Token_Throws_OperationCanceledException()
    {
        // GE-4: la cancelacion cooperativa se propaga (el CycleTaskService la
        // usa para detenerse de forma limpia).
        var dep = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime);

        Assert.ThrowsAny<OperationCanceledException>(() => dep.PayLoad(new CancellationToken(true)));
    }

    [Fact]
    public async Task PayLoad_Works_With_Reserved_Word_Table()
    {
        // CE-7: 'changetable(changes [Order], ...)' con identificadores
        // escapados (tabla con palabra reservada).
        TestTables.CreateCtOrder(_connection);
        var dep = _db.GetTableDependency(typeof(CtOrderItem), ChangeTrackingPriority.Realtime);
        var tcs = new TaskCompletionSource<DeltaChanges>(TaskCreationOptions.RunContinuationsAsynchronously);
        dep.OnChanged += (_, delta) => tcs.TrySetResult(delta);

        _connection.Execute("INSERT INTO [Order] ([Id], [User], [Quantity]) VALUES (1, 'A', 10)");

        Assert.Null(dep.PayLoad());
        var delta = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var change = Assert.Single(delta.Changes);
        Assert.Equal(ChangeOperation.Insert, change.Operation);
    }

    [Fact]
    public async Task Dispose_Stops_Delivery_And_PayLoad_Returns_Error()
    {
        // SD-17: al disponer la dependencia se completa el canal, no se entregan
        // mas deltas y PayLoad devuelve error.
        var dep = _db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime);
        var received = 0;
        dep.OnChanged += (_, _) => Interlocked.Increment(ref received);

        dep.Dispose();
        dep.Dispose(); // idempotente

        _connection.Insert(new CtItem { Name = "A", Quantity = 1 });

        var error = dep.PayLoad();
        Assert.NotNull(error);
        Assert.Contains("disposed", error);

        await Task.Delay(300);
        Assert.Equal(0, Volatile.Read(ref received));
    }

    [Fact]
    public void Database_Dispose_Is_Idempotent_And_External_Dispose_Is_NoOp()
    {
        // SD-17: solo el Database raiz dispone el cycle service; los hijos
        // (compartidos y cacheados) son no-op.
        var db = new SqlDb(_connection.ConnectionString);
        db.GetTableDependency(typeof(CtItem), ChangeTrackingPriority.Realtime); // arranca el bucle

        var ext = db.FactoryExternalDatabase("master");
        ext.Dispose();
        Assert.NotNull(ext.FactoryConnection()); // el hijo sigue siendo utilizable

        db.Dispose();
        db.Dispose(); // idempotente
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
        // SD-17: el repo se desuscribe del change tracking al disponerse; la
        // dependencia y la BD compartidas siguen vivas.
        var repo = new CtRepo(_db);
        var dep = repo.TableDependency;
        Assert.Equal(1, HandlerCount(dep));

        repo.Dispose();

        Assert.Equal(0, HandlerCount(dep));
        Assert.Null(dep.PayLoad()); // la dependencia sigue operativa
    }

    private static int HandlerCount(SqlTableDependency dep)
    {
        // El backing field de un event field-like es privado.
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
