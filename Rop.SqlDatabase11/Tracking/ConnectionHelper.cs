using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Rop.Database11
{
    /// <summary>
    /// Extension methods for polling SQL Server change tracking on a database connection.
    /// </summary>
    public static partial class ConnectionHelper
    {
        /// <summary>
        /// Gets the current change tracking version of the database.
        /// </summary>
        /// <param name="conn">Open database connection.</param>
        /// <returns>Result with the current change tracking version, or -1 when change tracking is not enabled.</returns>
        public static Result<long> GetDbVersion(this DbConnection conn)
        {
            try
            {
                var r = conn.ExecuteScalar("SELECT CHANGE_TRACKING_CURRENT_VERSION();");
                return (r is long l) ? l : -1;
            }
            catch (Exception ex)
            {
                return new ExceptionError(ex);
            }
        }
        /// <summary>
        /// Gets the current change tracking version of the database asynchronously.
        /// </summary>
        /// <param name="conn">Open database connection.</param>
        /// <param name="cancellationToken">Cancellation token checked before querying.</param>
        /// <returns>Task result with the current change tracking version, or -1 when change tracking is not enabled.</returns>
        public static async Task<Result<long>> GetDbVersionAsync(this DbConnection conn, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var r = await conn.ExecuteScalarAsync("SELECT CHANGE_TRACKING_CURRENT_VERSION();");
                return (r is long l) ? l : -1;
            }
            catch (Exception ex)
            {
                return new ExceptionError(ex);
            }
        }

        /// <summary>
        /// Gets the current change tracking version, switching to the foreign database when the key description references one.
        /// </summary>
        /// <param name="conn">Open database connection.</param>
        /// <param name="td">Key description of the table.</param>
        /// <returns>Result with the current change tracking version of the target database.</returns>
        public static Result<long> GetDbVersion(this DbConnection conn,KeyDescription td)
        {
            var use = td.GetUse();
            return GetDbVersion(conn, use);
        }
        /// <summary>
        /// Gets the current change tracking version asynchronously, switching to the foreign database when the key description references one.
        /// </summary>
        /// <param name="conn">Open database connection.</param>
        /// <param name="td">Key description of the table.</param>
        /// <param name="cancellationToken">Cancellation token checked before querying.</param>
        /// <returns>Task result with the current change tracking version of the target database.</returns>
        public static async Task<Result<long>> GetDbVersionAsync(this DbConnection conn, KeyDescription td, CancellationToken cancellationToken = default)
        {
            var use = td.GetUse();
            return await GetDbVersionAsync(conn, use, cancellationToken);
        }

        /// <summary>
        /// Gets the current change tracking version, prefixing the query with the given USE statement.
        /// </summary>
        /// <param name="conn">Open database connection.</param>
        /// <param name="use">USE statement prefix for a foreign database, or an empty string.</param>
        /// <returns>Result with the current change tracking version of the target database.</returns>
        public static Result<long> GetDbVersion(this DbConnection conn,string use)
        {
            try
            {
                var r = conn.ExecuteScalar($"{use}SELECT CHANGE_TRACKING_CURRENT_VERSION();");
                return (r is long l) ? l : 0;
            }
            catch (Exception ex)
            {
                return new ExceptionError(ex);
            }
        }
        /// <summary>
        /// Gets the current change tracking version asynchronously, prefixing the query with the given USE statement.
        /// </summary>
        /// <param name="conn">Open database connection.</param>
        /// <param name="use">USE statement prefix for a foreign database, or an empty string.</param>
        /// <param name="cancellationToken">Cancellation token checked before querying.</param>
        /// <returns>Task result with the current change tracking version of the target database.</returns>
        public static async Task<Result<long>> GetDbVersionAsync(this DbConnection conn, string use, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var r = await conn.ExecuteScalarAsync($"{use}SELECT CHANGE_TRACKING_CURRENT_VERSION();");
                return (r is long l) ? l : 0;
            }
            catch (Exception ex)
            {
                return new ExceptionError(ex);
            }
        }
        /// <summary>
        /// Polls CHANGETABLE for the rows changed in the table since the given version.
        /// </summary>
        /// <param name="conn">Open database connection.</param>
        /// <param name="lastv">Last processed change tracking version.</param>
        /// <param name="td">Key description of the table to poll.</param>
        /// <returns>Result with the delta of changes since the last version.</returns>
        public static Result<DeltaChanges> GetTableChanges(this DbConnection conn,long lastv,KeyDescription td)
        {
            var use = td.GetUse();
            // CE-7: identificadores escapados.
            // ReSharper disable once StringLiteralTypo
            var cmdStr = (!(td is PartialKeyDescription ptd))
                ? $"{use}SELECT {DapperHelperExtend.SqlName(td.KeyName)} as Id,SYS_CHANGE_VERSION,SYS_CHANGE_CREATION_VERSION,SYS_CHANGE_OPERATION FROM changetable(changes {DapperHelperExtend.SqlName(td.TableName)},@lastversion) as CT ORDER BY SYS_CHANGE_VERSION"
                : $"{use}SELECT {DapperHelperExtend.SqlName(ptd.KeyName)} as Id, {DapperHelperExtend.SqlName(ptd.Key2Name)} as Id2 ,SYS_CHANGE_VERSION,SYS_CHANGE_CREATION_VERSION,SYS_CHANGE_OPERATION FROM changetable(changes {DapperHelperExtend.SqlName(ptd.TableName)},@lastversion) as CT ORDER BY SYS_CHANGE_VERSION";
            var r = conn.Query<ChangeRowDto>(cmdStr,new {lastversion=lastv}).ToList();
            if (r.Count == 0) return DeltaChanges.Empty(td,lastv);
            var cv = r.Max(cr => cr.SYS_CHANGE_VERSION);
            var all = r.Select(dto => dto.ToChangeRow(td)).Where(c => c.Operation != ChangeOperation.Unknown);
            return new DeltaChanges(td,cv, lastv, all);
        }
        /// <summary>
        /// Polls CHANGETABLE asynchronously for the rows changed in the table since the given version.
        /// </summary>
        /// <param name="conn">Open database connection.</param>
        /// <param name="lastv">Last processed change tracking version.</param>
        /// <param name="td">Key description of the table to poll.</param>
        /// <param name="cancellationToken">Cancellation token checked before querying.</param>
        /// <returns>Task result with the delta of changes since the last version.</returns>
        public static async Task<Result<DeltaChanges>> GetTableChangesAsync(this DbConnection conn, long lastv, KeyDescription td, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var use = td.GetUse();
            // CE-7: identificadores escapados.
            // ReSharper disable once StringLiteralTypo
            var cmdStr = (!(td is PartialKeyDescription ptd))
                ? $"{use}SELECT {DapperHelperExtend.SqlName(td.KeyName)} as Id,SYS_CHANGE_VERSION,SYS_CHANGE_CREATION_VERSION,SYS_CHANGE_OPERATION FROM changetable(changes {DapperHelperExtend.SqlName(td.TableName)},@lastversion) as CT ORDER BY SYS_CHANGE_VERSION"
                : $"{use}SELECT {DapperHelperExtend.SqlName(ptd.KeyName)} as Id, {DapperHelperExtend.SqlName(ptd.Key2Name)} as Id2 ,SYS_CHANGE_VERSION,SYS_CHANGE_CREATION_VERSION,SYS_CHANGE_OPERATION FROM changetable(changes {DapperHelperExtend.SqlName(ptd.TableName)},@lastversion) as CT ORDER BY SYS_CHANGE_VERSION";
            var r = (await conn.QueryAsync<ChangeRowDto>(cmdStr, new { lastversion = lastv })).ToList();
            if (r.Count == 0) return DeltaChanges.Empty(td,lastv);
            var cv = r.Max(cr => cr.SYS_CHANGE_VERSION);
            var all = r.Select(dto => dto.ToChangeRow(td)).Where(c => c.Operation != ChangeOperation.Unknown);
            return new DeltaChanges(td,cv, lastv, all);
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private class ChangeRowDto
        {
            // SD-19: object en lugar de dynamic (sin binder de runtime por fila);
            // la conversion al tipo real de la clave ocurre en ToChangeRow.
            public object Id { get; init; } = null!;
            public object? Id2 { get; init; } = null;
            public long SYS_CHANGE_VERSION { get; init; }
            public long SYS_CHANGE_CREATION_VERSION { get; init; }
            public string SYS_CHANGE_OPERATION { get; init; } = "";
            public ChangeRow ToChangeRow(KeyDescription td)
            {
                var op = ChangeOperation.Unknown;
                switch (SYS_CHANGE_OPERATION)
                {
                    case "D": op = ChangeOperation.Delete;
                        break;
                    case "I": op = ChangeOperation.Insert;
                        break;
                    case "U": op = ChangeOperation.Update;
                        break;
                }
                var id = DeltaChanges.ConvertKeyValue(Id, td.KeyType);
                object? id2 = null;
                if (td is PartialKeyDescription ptd && Id2 is not null)
                {
                    id2 = DeltaChanges.ConvertKeyValue(Id2, ptd.Key2Prop.PropertyType);
                }
                return new ChangeRow(id, id2, op);
            }
        }
    }
}
