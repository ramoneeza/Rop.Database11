using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Rop.Database11.Tracking;

namespace Rop.Database11
{

    /// <summary>
    /// Per-table change tracking poller registered in a cycle service. It raises
    /// <see cref="OnChanged"/> with the detected deltas, in order and serialized.
    /// Disposing it completes the delivery channel so pending deltas are flushed.
    /// </summary>
    public class SqlTableDependency:ICycleTask,IDisposable
    {
        private readonly ILogger _logger;
        private bool _disposed;
        /// <summary>
        /// Unique key of the dependency, composed of database and table name.
        /// </summary>
        public string Key { get; }
        /// <summary>
        /// Interval between polls, derived from the priority.
        /// </summary>
        public TimeSpan Interval { get; }
        /// <summary>
        /// Database used to poll the table.
        /// </summary>
        public Database Database { get; }
        /// <summary>
        /// Key description of the tracked table.
        /// </summary>
        public KeyDescription TableDescription { get; }
        /// <summary>
        /// Priority of the dependency in the cycle service.
        /// </summary>
        public ChangeTrackingPriority Priority { get;  }
        /// <summary>
        /// Last known change tracking version of the table.
        /// </summary>
        public TrackingVersion TableVersion { get; private set; }
        /// <summary>
        /// Raised for every non-empty delta detected since the last poll.
        /// </summary>
        public event EventHandler<DeltaChanges>? OnChanged;

        // Cambiado a internal para que Database (mismo ensamblado) pueda instanciarlo.
        internal SqlTableDependency(Database database, Type t, ChangeTrackingPriority priority = ChangeTrackingPriority.Default)
        {
            var name = CalcKey(database, t);
            Key = name;
            Priority = priority;
            if ((int)Priority > 10) throw new ArgumentException("Priority from 0 (maximum) to 10 (minimum)");
            Interval =Priority.ToInterval();
            _logger = database.Logger;
            TableDescription = DapperHelperExtend.GetAnyKeyDescription(t) ?? throw new ArgumentException($"type {t} has not any kind of key");
            Database = database.FactoryExternalDatabase(TableDescription);
            var tableversion= Database.GetDbVersion(TableDescription).ValueOrThrow();
            TableVersion = new TrackingVersion
            {
                Version = tableversion,
                Timestamp = DateTimeOffset.Now
            };
        }
        /// <summary>
        /// Polls the change tracking table. If the token is cancelled, throws
        /// <see cref="OperationCanceledException"/> (the cycle service uses it
        /// to stop cleanly).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token checked before querying.</param>
        /// <returns>Error message if the payload failed, null on success.</returns>
        public string? PayLoad(CancellationToken cancellationToken = default)
        {
            if (_disposed) return "SqlTableDependency is disposed";
            cancellationToken.ThrowIfCancellationRequested();
            using var sqlConnection = Database.FactoryConnection();
            try
            {
                sqlConnection.Open();
                var r = sqlConnection.GetTableChanges(TableVersion.Version, TableDescription);
                var deltaChanges = r.ValueOrThrow();
                if (deltaChanges.IsNewVersion)
                {
                    // SD-7: avanzar SIEMPRE que haya version nueva, aunque el delta
                    // venga vacio (cambios filtrados como Unknown): sin avanzar se
                    // releerian las mismas filas indefinidamente.
                    TableVersion = new TrackingVersion
                    {
                        Version = deltaChanges.Version,
                        Timestamp = DateTimeOffset.Now
                    };
                    if (!deltaChanges.IsEmpty) _sendChanges(deltaChanges);
                }
                return null;
            }
            catch (OperationCanceledException)
            {
                // La cancelacion se propaga al bucle del CycleTaskService, que la
                // usa para detenerse de forma limpia.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Change tracking PayLoad failed for table {Table}", TableDescription.TableName);
                return ex.Message;
            }
            finally
            {
                sqlConnection.Close();
            }
        }

        /// <summary>
        /// Completes the change channel: the background reader delivers the
        /// remaining deltas and exits. The dependency cannot be used afterwards
        /// (PayLoad returns an error message).
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _changes.Writer.TryComplete();
        }

        private readonly Channel<DeltaChanges> _changes = Channel.CreateUnbounded<DeltaChanges>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
        private readonly Lock _readerLock = new();
        private Task? _readerTask;

        private void _sendChanges(DeltaChanges deltaChanges)
        {
            if (_disposed) return;
            if (OnChanged == null) return;
            // SD-8: cola FIFO por dependencia con un unico lector: los deltas se
            // entregan en orden y serializados (antes: un Task.Run por delta,
            // sin orden ni backpressure).
            _changes.Writer.TryWrite(deltaChanges);
            if (_readerTask is not null) return;
            lock (_readerLock)
            {
                if (_readerTask is not null) return;
                _readerTask = Task.Run(async () =>
                {
                    await foreach (var delta in _changes.Reader.ReadAllAsync())
                    {
                        var handler = OnChanged;
                        if (handler == null) continue;
                        foreach (var d in handler.GetInvocationList())
                        {
                            if (d is not EventHandler<DeltaChanges> h) continue;
                            try
                            {
                                h.Invoke(this, delta);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "OnChanged handler failed for table {Table}", TableDescription.TableName);
                            }
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Computes the dependency key for a database and entity type.
        /// </summary>
        /// <param name="database">Database owning the table.</param>
        /// <param name="t">Entity type of the table.</param>
        /// <returns>Key composed of the database and table name.</returns>
        public static string CalcKey(Database database, Type t)
        {
            var td = database.GetAnyKeyDescription(t) ?? throw new ArgumentException($"type {t} has not any kind of key");
            var name = database.MainDatabaseName + "." + td.TableName;
            return name;
        }
    }
}