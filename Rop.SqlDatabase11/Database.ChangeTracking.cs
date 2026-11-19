namespace Rop.Database11
{
    public partial class Database
    {
        /// <summary>
        /// Returns the current change tracking version of the main database.
        /// </summary>
        /// <returns>Result with the current change tracking version.</returns>
        public Result<long> GetDbVersion()
        {
            return UnitOfWorkSingle(conn=>conn.GetDbVersion());
        }
        /// <summary>
        /// Returns the current change tracking version of the database that owns the specified table.
        /// </summary>
        /// <param name="table">Key description of the table.</param>
        /// <returns>Result with the current change tracking version.</returns>
        public Result<long> GetDbVersion(KeyDescription table)
        {
            return UnitOfWorkSingle(conn=>conn.GetDbVersion(table));
        }
        /// <summary>
        /// Returns the current change tracking version of the database identified by the specified USE command.
        /// </summary>
        /// <param name="use">USE command or database name identifying the target database.</param>
        /// <returns>Result with the current change tracking version.</returns>
        public Result<long> GetDbVersion(string use)
        {
            return UnitOfWorkSingle(conn=>conn.GetDbVersion(use));
        }

        /// <summary>
        /// Returns asynchronously the current change tracking version of the main database.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token propagated to the connection operations.</param>
        /// <returns>Task with a result containing the current change tracking version.</returns>
        public async Task<Result<long>> GetDbVersionAsync(CancellationToken cancellationToken = default)
        {
            return await UnitOfWorkSingleAsync(conn => conn.GetDbVersionAsync(cancellationToken), cancellationToken);
        }
        /// <summary>
        /// Returns asynchronously the current change tracking version of the database that owns the specified table.
        /// </summary>
        /// <param name="table">Key description of the table.</param>
        /// <param name="cancellationToken">Cancellation token propagated to the connection operations.</param>
        /// <returns>Task with a result containing the current change tracking version.</returns>
        public async Task<Result<long>> GetDbVersionAsync(KeyDescription table, CancellationToken cancellationToken = default)
        {
            return await UnitOfWorkSingleAsync(conn => conn.GetDbVersionAsync(table, cancellationToken), cancellationToken);
        }
        /// <summary>
        /// Returns asynchronously the current change tracking version of the database identified by the specified USE command.
        /// </summary>
        /// <param name="use">USE command or database name identifying the target database.</param>
        /// <param name="cancellationToken">Cancellation token propagated to the connection operations.</param>
        /// <returns>Task with a result containing the current change tracking version.</returns>
        public async Task<Result<long>> GetDbVersionAsync(string use, CancellationToken cancellationToken = default)
        {
            return await UnitOfWorkSingleAsync(conn => conn.GetDbVersionAsync(use, cancellationToken), cancellationToken);
        }
        
        /// <summary>
        /// Verifies that change tracking is properly configured for the specified table.
        /// </summary>
        /// <param name="td">Key description of the table.</param>
        /// <returns>VoidResult indicating success or the failure details.</returns>
        public VoidResult VerifyNotify(KeyDescription td)
        {
            var r = GetDbVersion(td);
            if (r.IsFailed) return r;
            var r2=GetTableChanges(0,td);
            return r2;
        }

        /// <summary>
        /// Verifies that change tracking is properly configured for the table of the specified entity type.
        /// </summary>
        /// <typeparam name="T">Entity type whose table will be checked.</typeparam>
        /// <returns>VoidResult indicating success or the failure details.</returns>
        public VoidResult VerifyNotify<T>() where T : class
        {
            var td=GetAnyKeyDescription<T>();
            if (td == null) return new Error($"Table {typeof(T).Name} not found");
            return VerifyNotify(td);
        }

        /// <summary>
        /// Verifies asynchronously that change tracking is properly configured for the specified table.
        /// </summary>
        /// <param name="td">Key description of the table.</param>
        /// <param name="cancellationToken">Cancellation token propagated to the connection operations.</param>
        /// <returns>Task with a VoidResult indicating success or the failure details.</returns>
        public async Task<VoidResult> VerifyNotifyAsync(KeyDescription td, CancellationToken cancellationToken = default)
        {
            var r = await GetDbVersionAsync(td, cancellationToken);
            if (r.IsFailed) return r;
            var r2 = await GetTableChangesAsync(0, td, cancellationToken);
            return r2;
        }

        /// <summary>
        /// Verifies asynchronously that change tracking is properly configured for the table of the specified entity type.
        /// </summary>
        /// <typeparam name="T">Entity type whose table will be checked.</typeparam>
        /// <param name="cancellationToken">Cancellation token propagated to the connection operations.</param>
        /// <returns>Task with a VoidResult indicating success or the failure details.</returns>
        public async Task<VoidResult> VerifyNotifyAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            var td = GetAnyKeyDescription<T>();
            if (td == null) return new Error($"Table {typeof(T).Name} not found");
            return await VerifyNotifyAsync(td, cancellationToken);
        }

        /// <summary>
        /// Verifies that change tracking is configured for the specified table and throws when the verification fails.
        /// </summary>
        /// <param name="td">Key description of the table.</param>
        public void VerifyThrowNotify(KeyDescription td)
        {
            VerifyNotify(td).ThrowIfFailed();
        }
        /// <summary>
        /// Verifies that change tracking is configured for the table of the specified entity type and throws when the verification fails.
        /// </summary>
        /// <typeparam name="T">Entity type whose table will be checked.</typeparam>
        public void VerifyThrowNotify<T>()
        {
            var td=GetAnyKeyDescription<T>()??throw new Exception($"Table for {typeof(T).Name} not found");
            VerifyThrowNotify(td);
        }

        /// <summary>
        /// Verifies asynchronously that change tracking is configured for the specified table and throws when the verification fails.
        /// </summary>
        /// <param name="td">Key description of the table.</param>
        /// <param name="cancellationToken">Cancellation token propagated to the connection operations.</param>
        /// <returns>Task that completes when the verification finishes.</returns>
        public async Task VerifyThrowNotifyAsync(KeyDescription td, CancellationToken cancellationToken = default)
        {
            var r = await VerifyNotifyAsync(td, cancellationToken);
            r.ThrowIfFailed();
        }

        /// <summary>
        /// Verifies asynchronously that change tracking is configured for the table of the specified entity type and throws when the verification fails.
        /// </summary>
        /// <typeparam name="T">Entity type whose table will be checked.</typeparam>
        /// <param name="cancellationToken">Cancellation token propagated to the connection operations.</param>
        /// <returns>Task that completes when the verification finishes.</returns>
        public async Task VerifyThrowNotifyAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            var td = GetAnyKeyDescription<T>() ?? throw new Exception($"Table for {typeof(T).Name} not found");
            await VerifyThrowNotifyAsync(td, cancellationToken);
        }

        /// <summary>
        /// Returns the changes recorded for a table since the specified change tracking version.
        /// </summary>
        /// <param name="lasttableVersion">Last known change tracking version.</param>
        /// <param name="td">Key description of the table.</param>
        /// <returns>Result with the delta changes of the table.</returns>
        public Result<DeltaChanges> GetTableChanges(long lasttableVersion,KeyDescription td)
        {
            return UnitOfWorkSingle(conn => conn.GetTableChanges(lasttableVersion,td));
        }
        
        /// <summary>
        /// Returns asynchronously the changes recorded for a table since the specified change tracking version.
        /// </summary>
        /// <param name="lasttableVersion">Last known change tracking version.</param>
        /// <param name="td">Key description of the table.</param>
        /// <param name="cancellationToken">Cancellation token propagated to the connection operations.</param>
        /// <returns>Task with a result containing the delta changes of the table.</returns>
        public async Task<Result<DeltaChanges>> GetTableChangesAsync(long lasttableVersion, KeyDescription td, CancellationToken cancellationToken = default)
        {
            return await UnitOfWorkSingleAsync(conn => conn.GetTableChangesAsync(lasttableVersion, td, cancellationToken), cancellationToken);
        }


       
    }
}
