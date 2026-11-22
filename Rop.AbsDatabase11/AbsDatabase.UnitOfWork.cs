using System.Data.Common;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Rop.Database;


public partial class AbsDatabase
{

    // ===== Synchronous UnitOfWork: 8 combinations =====
    // Inputs: Conn only / Conn + Tr
    // Function outputs: Result<T> / EnumerableResult<T> / IEnumerable<T> / T?
    // (IEnumerable<T> and T? are wrapped so the error can be returned and rollback performed)
    //
    // Conn only:
    //  1. Conn -> Result<T>
    //  2. Conn -> EnumerableResult<T>
    //  3. Conn -> IEnumerable<T>
    //  4. Conn -> T?
    // Conn + Tr:
    //  5. (Conn, Tr) -> Result<T>
    //  6. (Conn, Tr) -> EnumerableResult<T>
    //  7. (Conn, Tr) -> IEnumerable<T>
    //  8. (Conn, Tr) -> T?



    // Normalizes cancellation of the caller's token.
    //
    // Cancellation is propagated as an EXCEPTION (OperationCanceledException) and not as an Error:
    // it is a control-flow signal, not a business failure. The caller distinguishes it
    // from errors because the exception carries the associated token (e.CancellationToken).
    //
    // Providers (e.g. SqlClient) may surface cancellation as a SqlException
    // instead of an OperationCanceledException, so here it is normalized to a single form:
    //
    // - If the exception is ALREADY an OperationCanceledException, it is rethrown as-is via
    //   ExceptionDispatchInfo.Throw to preserve its original stack trace.
    //   (`throw;` cannot be used because this method is a helper and is not inside the catch.)
    // - If it is any other exception (e.g. a SqlException caused by the cancellation),
    //   it is wrapped in a new OperationCanceledException with the exception as inner
    //   and the token attached.
    private static Exception _toCancellation(Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException) ExceptionDispatchInfo.Throw(ex);
        return new OperationCanceledException("The operation was cancelled.", ex, ct);
    }

    private Error _tryRollBack(DbTransaction tr, Error ex)
    {
        try
        {
            tr.Rollback();
            return ex;
        }
        catch (Exception e)
        {
            if (ex is ExceptionError eerror)
            {
                var agg=new AggregateException($"Error rolling back transaction. Original exception: {eerror.Data.Message}. Rollback exception: {e.Message}", eerror.Data, e);
                return Error.Exception(agg);
            }
            return Error.Exception(new Exception($"Error rolling back transaction. Original exception: {ex.Value}. Rollback exception: {e.Message}", e));
        }
    }
    private async Task<Error> _tryRollBackAsync(DbTransaction tr, Error ex) 
    {
        try
        {
            await tr.RollbackAsync();
            return ex;
        }
        catch (Exception e)
        {
            if (ex is ExceptionError eerror)
            {
                var agg = new AggregateException($"Error rolling back transaction. Original exception: {eerror.Data.Message}. Rollback exception: {e.Message}", eerror.Data, e);
                return Error.Exception(agg);
            }
            return Error.Exception(new Exception($"Error rolling back transaction. Original exception: {ex.Value}. Rollback exception: {e.Message}", e));
        }
    }
    // AB-8: "silent" rollback when the operation is about to be retried with a new
    // connection/transaction: a rollback failure must not prevent the retry.
    private static void _silentRollBack(DbTransaction tr)
    {
        try { tr.Rollback(); } catch { /* ignored: the connection is doomed and will be retried with a new one */ }
    }
    private static async Task _silentRollBackAsync(DbTransaction tr)
    {
        try { await tr.RollbackAsync(); } catch { /* ignored: the connection is doomed and will be retried with a new one */ }
    }

    // AB-8: the _catch helpers convert exceptions into a failed Result, BUT rethrow
    // the transient ones so the UnitOfWork core decides whether to retry
    // (with the attempt number) or give up. This way the transient classification
    // is not lost in the wrapper.
    private Result<T> _catch<T>(DbConnection conn,DbTransaction tr, Func<DbConnection, DbTransaction, T> funcion)
    {
        try
        {
            var t=funcion(conn, tr);
            return t;
        }
        catch (Exception e)
        {
            if (RetryPolicy.IsTransientError(e)) throw;
            return Error.Exception(e);
        }
    }

    private async Task<Result<T>> _catchAsync<T>(DbConnection conn, DbTransaction tr,CancellationToken ct, Func<DbConnection, DbTransaction, CancellationToken, Task<T>> funcion)
    {
        try
        {
            var t = await funcion(conn, tr, ct);
            return t;
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            if (RetryPolicy.IsTransientError(e)) throw;
            return Error.Exception(e);
        }
    }


    private Result<T> _catch<T>(DbConnection conn, Func<DbConnection, T?> funcion)
    {
        try
        {
            var t = funcion(conn);
            return new(t);
        }
        catch (Exception e)
        {
            if (RetryPolicy.IsTransientError(e)) throw;
            return Error.Exception(e);
        }
    }
    private async Task<Result<T>> _catchAsync<T>(DbConnection conn, CancellationToken ct, Func<DbConnection, CancellationToken, Task<T?>> funcion)
    {
        try
        {
            var t = await funcion(conn, ct);
            return new(t);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            if (RetryPolicy.IsTransientError(e)) throw;
            return Error.Exception(e);
        }
    }
    private EnumerableResult<T> _catchEnumerable<T>(DbConnection conn,DbTransaction tr, Func<DbConnection, DbTransaction, IEnumerable<T>> funcion)
    {
        try
        {
            var t = funcion(conn, tr);
            return new(t);
        }
        catch (Exception e)
        {
            if (RetryPolicy.IsTransientError(e)) throw;
            return Error.Exception(e);
        }
    }
    private async Task<EnumerableResult<T>> _catchEnumerableAsync<T>(DbConnection conn, DbTransaction tr, CancellationToken ct, Func<DbConnection, DbTransaction, CancellationToken, Task<IEnumerable<T>>> funcion)
    {
        try
        {
            var t = await funcion(conn, tr, ct);
            return new(t);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            if (RetryPolicy.IsTransientError(e)) throw;
            return Error.Exception(e);
        }
    }


    private EnumerableResult<T> _catchEnumerable<T>(DbConnection conn, Func<DbConnection, IEnumerable<T>> funcion)
    {
        try
        {
            var t = funcion(conn);
            return new(t);
        }
        catch (Exception e)
        {
            if (RetryPolicy.IsTransientError(e)) throw;
            return Error.Exception(e);
        }
    }
    private async Task<EnumerableResult<T>> _catchEnumerableAsync<T>(DbConnection conn, CancellationToken ct, Func<DbConnection, CancellationToken, Task<IEnumerable<T>>> funcion)
    {
        try
        {
            var t = await funcion(conn, ct);
            return new(t);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            if (RetryPolicy.IsTransientError(e)) throw;
            return Error.Exception(e);
        }
    }

    // ===== Synchronous cores (transactional logic, defined once) =====
    // TR can be Result<R> or EnumerableResult<R> (both inherit from AbsResult).

    private TR _UnitOfWorkWithTrCore<TR>(Func<DbConnection, DbTransaction, TR> funcion, Func<Error, TR> onFailed) where TR : AbsResult
    {
        // AB-8: retry on transient errors. Each attempt uses a NEW connection and
        // transaction; a failed Result (business error) is not retried.
        var policy = RetryPolicy;
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                using var conn = FactoryConnection();
                try
                {
                    conn.Open();
                    using var tr = conn.BeginTransaction();
                    try
                    {
                        var res = funcion(conn, tr);
                        if (res.IsFailed) return onFailed(_tryRollBack(tr, res.Error!));
                        tr.Commit();
                        return res;
                    }
                    catch (Exception ex)
                    {
                        if (policy.CanRetry(ex, attempt))
                        {
                            _silentRollBack(tr);
                            Thread.Sleep(policy.DelayFor(attempt));
                            continue;
                        }
                        return onFailed(_tryRollBack(tr, Error.Exception(ex)));
                    }
                }
                finally
                {
                    conn.Close();
                }
            }
            catch (Exception ex)
            {
                if (policy.CanRetry(ex, attempt))
                {
                    Thread.Sleep(policy.DelayFor(attempt));
                    continue;
                }
                return onFailed(Error.Exception(ex));
            }
        }
    }

    private TR _UnitOfWorkNoTrCore<TR>(Func<DbConnection, TR> funcion, Func<Error, TR> onFailed) where TR : AbsResult
    {
        // AB-8: retry on transient errors (reads; internal writes
        // go through the transactional path). Each attempt uses a NEW connection.
        var policy = RetryPolicy;
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                using var conn = FactoryConnection();
                conn.Open();
                try
                {
                    return funcion(conn);
                }
                catch (Exception ex)
                {
                    if (policy.CanRetry(ex, attempt))
                    {
                        Thread.Sleep(policy.DelayFor(attempt));
                        continue;
                    }
                    return onFailed(Error.Exception(ex));
                }
                finally
                {
                    conn.Close();
                }
            }
            catch (Exception ex)
            {
                if (policy.CanRetry(ex, attempt))
                {
                    Thread.Sleep(policy.DelayFor(attempt));
                    continue;
                }
                return onFailed(Error.Exception(ex));
            }
        }
    }

    // ===== The 8 synchronous combinations (thin adapters over the cores) =====

    // 5. (Conn, Tr) -> Result<T>
    private Result<R> _UnitOfWorkWithTr<R, T>(Func<DbConnection, DbTransaction, Result<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkWithTrCore((conn, tr) => funcion(conn, tr).Map(map), e => e);
    }

    // 8. (Conn, Tr) -> T  → wrapped in Result<T> so rollback can be performed on error
    private Result<R> _UnitOfWorkWithTr<R, T>(Func<DbConnection, DbTransaction, T> funcion, Func<T, R> map)
    {
        return _UnitOfWorkWithTrCore((conn, tr) => _catch(conn, tr, funcion).Map(map), e => e);
    }

    // 6. (Conn, Tr) -> EnumerableResult<T>
    private EnumerableResult<R> _UnitOfWorkWithTr<R, T>(Func<DbConnection, DbTransaction, EnumerableResult<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkWithTrCore((conn, tr) => funcion(conn, tr).Map(map), e => e);
    }

    // 7. (Conn, Tr) -> IEnumerable<T>  → wrapped in EnumerableResult<T> so rollback can be performed on error
    private EnumerableResult<R> _UnitOfWorkWithTr<R, T>(Func<DbConnection, DbTransaction, IEnumerable<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkWithTrCore((conn, tr) => _catchEnumerable(conn, tr, funcion).Map(map), e => e);
    }

    // 1. Conn -> Result<T>
    private Result<R> _UnitOfWorkNoTr<R, T>(Func<DbConnection, Result<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkNoTrCore(conn => funcion(conn).Map(map), e => e);
    }

    // 4. Conn -> T?  → wrapped in Result<T> so the error can be returned on exception
    private Result<R> _UnitOfWorkNoTr<R, T>(Func<DbConnection, T?> funcion, Func<T, R> map)
    {
        return _UnitOfWorkNoTrCore(conn => _catch(conn, funcion).Map(map), e => e);
    }

    // 2. Conn -> EnumerableResult<T>
    private EnumerableResult<R> _UnitOfWorkNoTr<R, T>(Func<DbConnection, EnumerableResult<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkNoTrCore(conn => funcion(conn).Map(map), e => e);
    }

    // 3. Conn -> IEnumerable<T>  → wrapped in EnumerableResult<T> so the error can be returned on exception
    private EnumerableResult<R> _UnitOfWorkNoTr<R, T>(Func<DbConnection, IEnumerable<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkNoTrCore(conn => _catchEnumerable(conn, funcion).Map(map), e => e);
    }

    // ===== Async: 16 combinations =====
    // Inputs: Conn only / Conn + Tr
    // Function outputs: Result<T> / EnumerableResult<T> / IEnumerable<T> / T?
    // The function may or may not accept a CancellationToken (2 variants).
    //
    // Conn only:
    //  1. Conn -> Result<T>                       (no ct)
    //  2. Conn -> Result<T>                       (with ct)
    //  3. Conn -> EnumerableResult<T>             (no ct)
    //  4. Conn -> EnumerableResult<T>             (with ct)
    //  5. Conn -> IEnumerable<T>                  (no ct)
    //  6. Conn -> IEnumerable<T>                  (with ct)
    //  7. Conn -> T?                              (no ct)
    //  8. Conn -> T?                              (with ct)
    // Conn + Tr:
    //  9.  (Conn, Tr) -> Result<T>                (no ct)
    // 10.  (Conn, Tr) -> Result<T>                (with ct)
    // 11.  (Conn, Tr) -> EnumerableResult<T>      (no ct)
    // 12.  (Conn, Tr) -> EnumerableResult<T>      (with ct)
    // 13.  (Conn, Tr) -> IEnumerable<T>           (no ct)
    // 14.  (Conn, Tr) -> IEnumerable<T>           (with ct)
    // 15.  (Conn, Tr) -> T?                       (no ct)
    // 16.  (Conn, Tr) -> T?                       (with ct)
    //
    // Decisions: rollback and commit run without a token (rollback must not be
    // left half-done; commit must not leave the result indeterminate).

    // ===== Async cores (transactional logic, defined once) =====
    // TR can be Result<R> or EnumerableResult<R> (both inherit from AbsResult).

    private async Task<TR> _UnitOfWorkWithTrCoreAsync<TR>(Func<DbConnection, DbTransaction, CancellationToken, Task<TR>> funcion, Func<Error, TR> onFailed, CancellationToken ct) where TR : AbsResult
    {
        // AB-8: retry on transient errors. Each attempt uses a NEW connection and
        // transaction; a failed Result (business error) is not retried.
        // Cancellation takes priority over retry.
        var policy = RetryPolicy;
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await using var conn = FactoryConnection();
                await conn.OpenAsync(ct);
                try
                {
                    await using var tr = await conn.BeginTransactionAsync(ct);
                    try
                    {
                        var res = await funcion(conn, tr, ct);
                        if (res.IsFailed) return onFailed(await _tryRollBackAsync(tr, res.Error!));
                        // Commit without a token: if it were cancelled during the commit, the result would be indeterminate.
                        await tr.CommitAsync();
                        return res;
                    }
                    catch (Exception ex) when (ct.IsCancellationRequested)
                    {
                        try { await tr.RollbackAsync(); }
                        catch (Exception rbex) { Debug.WriteLine($"Rollback failed after cancellation: {rbex.Message}"); }
                        throw _toCancellation(ex, ct);
                    }
                    catch (Exception ex)
                    {
                        if (policy.CanRetry(ex, attempt))
                        {
                            await _silentRollBackAsync(tr);
                            await Task.Delay(policy.DelayFor(attempt), ct);
                            continue;
                        }
                        return onFailed(await _tryRollBackAsync(tr, Error.Exception(ex)));
                    }
                }
                finally
                {
                    await conn.CloseAsync();
                }
            }
            catch (Exception ex) when (ct.IsCancellationRequested)
            {
                throw _toCancellation(ex, ct);
            }
            catch (Exception ex)
            {
                if (policy.CanRetry(ex, attempt))
                {
                    await Task.Delay(policy.DelayFor(attempt), ct);
                    continue;
                }
                return onFailed(Error.Exception(ex));
            }
        }
    }

    // ===== WithTr adapters (8) =====

    // 9. (Conn, Tr) -> Result<T> (no ct)
    private Task<Result<R>> _UnitOfWorkWithTrResultAsync<R, T>(Func<DbConnection, DbTransaction, Task<Result<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, _) => (await funcion(conn, tr)).Map(map), e => e, ct);
    }

    // 10. (Conn, Tr) -> Result<T> (with ct)
    private Task<Result<R>> _UnitOfWorkWithTrResultAsync<R, T>(Func<DbConnection, DbTransaction, CancellationToken, Task<Result<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await funcion(conn, tr, c)).Map(map), e => e, ct);
    }

    // 15. (Conn, Tr) -> T? (no ct)
    private Task<Result<R>> _UnitOfWorkWithTrValueAsync<R, T>(Func<DbConnection, DbTransaction, Task<T>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await _catchAsync(conn, tr, c, (conn2, tr2, _) => funcion(conn2, tr2))).Map(map), e => e, ct);
    }

    // 16. (Conn, Tr) -> T? (with ct)
    private Task<Result<R>> _UnitOfWorkWithTrValueAsync<R, T>(Func<DbConnection, DbTransaction, CancellationToken, Task<T>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await _catchAsync(conn, tr, c, funcion)).Map(map), e => e, ct);
    }

    // 11. (Conn, Tr) -> EnumerableResult<T> (no ct)
    private Task<EnumerableResult<R>> _UnitOfWorkWithTrEnumerableResultAsync<R, T>(Func<DbConnection, DbTransaction, Task<EnumerableResult<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, _) => (await funcion(conn, tr)).Map(map), e => e, ct);
    }

    // 12. (Conn, Tr) -> EnumerableResult<T> (with ct)
    private Task<EnumerableResult<R>> _UnitOfWorkWithTrEnumerableResultAsync<R, T>(Func<DbConnection, DbTransaction, CancellationToken, Task<EnumerableResult<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await funcion(conn, tr, c)).Map(map), e => e, ct);
    }

    // 13. (Conn, Tr) -> IEnumerable<T> (no ct)
    private Task<EnumerableResult<R>> _UnitOfWorkWithTrEnumerableAsync<R, T>(Func<DbConnection, DbTransaction, Task<IEnumerable<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await _catchEnumerableAsync(conn, tr, c, (conn2, tr2, _) => funcion(conn2, tr2))).Map(map), e => e, ct);
    }

    // 14. (Conn, Tr) -> IEnumerable<T> (with ct)
    private Task<EnumerableResult<R>> _UnitOfWorkWithTrEnumerableAsync<R, T>(Func<DbConnection, DbTransaction, CancellationToken, Task<IEnumerable<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await _catchEnumerableAsync(conn, tr, c, funcion)).Map(map), e => e, ct);
    }

    private async Task<TR> _UnitOfWorkNoTrCoreAsync<TR>(Func<DbConnection, CancellationToken, Task<TR>> funcion, Func<Error, TR> onFailed, CancellationToken ct) where TR : AbsResult
    {
        // AB-8: retry on transient errors (reads; internal writes
        // go through the transactional path). Each attempt uses a NEW connection.
        // Cancellation takes priority over retry.
        var policy = RetryPolicy;
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await using var conn = FactoryConnection();
                await conn.OpenAsync(ct);
                try
                {
                    return await funcion(conn, ct);
                }
                catch (Exception ex) when (ct.IsCancellationRequested)
                {
                    throw _toCancellation(ex, ct);
                }
                catch (Exception ex)
                {
                    if (policy.CanRetry(ex, attempt))
                    {
                        await Task.Delay(policy.DelayFor(attempt), ct);
                        continue;
                    }
                    return onFailed(Error.Exception(ex));
                }
                finally
                {
                    await conn.CloseAsync();
                }
            }
            catch (Exception ex) when (ct.IsCancellationRequested)
            {
                throw _toCancellation(ex, ct);
            }
            catch (Exception ex)
            {
                if (policy.CanRetry(ex, attempt))
                {
                    await Task.Delay(policy.DelayFor(attempt), ct);
                    continue;
                }
                return onFailed(Error.Exception(ex));
            }
        }
    }

    // ===== NoTr adapters (8) =====

    // 1. Conn -> Result<T> (no ct)
    private Task<Result<R>> _UnitOfWorkNoTrResultAsync<R, T>(Func<DbConnection, Task<Result<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, _) => (await funcion(conn)).Map(map), e => e, ct);
    }

    // 2. Conn -> Result<T> (with ct)
    private Task<Result<R>> _UnitOfWorkNoTrResultAsync<R, T>(Func<DbConnection, CancellationToken, Task<Result<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await funcion(conn, c)).Map(map), e => e, ct);
    }

    // 7. Conn -> T? (no ct)
    private Task<Result<R>> _UnitOfWorkNoTrValueAsync<R, T>(Func<DbConnection, Task<T?>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await _catchAsync(conn, c, (conn2, _) => funcion(conn2))).Map(map), e => e, ct);
    }

    // 8. Conn -> T? (with ct)
    private Task<Result<R>> _UnitOfWorkNoTrValueAsync<R, T>(Func<DbConnection, CancellationToken, Task<T?>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await _catchAsync(conn, c, funcion)).Map(map), e => e, ct);
    }

    // 3. Conn -> EnumerableResult<T> (no ct)
    private Task<EnumerableResult<R>> _UnitOfWorkNoTrEnumerableResultAsync<R, T>(Func<DbConnection, Task<EnumerableResult<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, _) => (await funcion(conn)).Map(map), e => e, ct);
    }

    // 4. Conn -> EnumerableResult<T> (with ct)
    private Task<EnumerableResult<R>> _UnitOfWorkNoTrEnumerableResultAsync<R, T>(Func<DbConnection, CancellationToken, Task<EnumerableResult<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await funcion(conn, c)).Map(map), e => e, ct);
    }

    // 5. Conn -> IEnumerable<T> (no ct)
    private Task<EnumerableResult<R>> _UnitOfWorkNoTrEnumerableAsync<R, T>(Func<DbConnection, Task<IEnumerable<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await _catchEnumerableAsync(conn, c, (conn2, _) => funcion(conn2))).Map(map), e => e, ct);
    }

    // 6. Conn -> IEnumerable<T> (with ct)
    private Task<EnumerableResult<R>> _UnitOfWorkNoTrEnumerableAsync<R, T>(Func<DbConnection, CancellationToken, Task<IEnumerable<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await _catchEnumerableAsync(conn, c, funcion)).Map(map), e => e, ct);
    }

    /// <summary>
    /// Executes a transactional unit of work that returns a <see cref="Result{T}"/>.
    /// The function is invoked with an open <see cref="DbConnection"/> and an active <see cref="DbTransaction"/>.
    /// The transaction is committed when the result is successful; otherwise it is rolled back.
    /// </summary>
    /// <typeparam name="T">Type returned by the operation.</typeparam>
    /// <param name="funcion">Function that performs the operation using the connection and transaction.</param>
    /// <returns>A <see cref="Result{T}"/> produced by the function or an exception result if an error occurs.</returns>
    public Result<T> UnitOfWorkSingle<T>(Func<DbConnection, DbTransaction, Result<T>> funcion)
    {
        return _UnitOfWorkWithTr<T,T>(funcion, r => r);
    }
    
    /// <summary>
    /// Executes a transactional unit of work that returns a plain value of type <typeparamref name="T"/>.
    /// The function is invoked with an open <see cref="DbConnection"/> and an active <see cref="DbTransaction"/>.
    /// On success the transaction is committed; on exception it is rolled back and an exception result is returned.
    /// </summary>
    /// <typeparam name="T">Type returned by the operation.</typeparam>
    /// <param name="funcion">Function that performs the operation using the connection and transaction.</param>
    /// <returns>A <see cref="Result{T}"/> wrapping the returned value or an exception result.</returns>
    public Result<T> UnitOfWorkSingle<T>(Func<DbConnection, DbTransaction, T> funcion)
    {
        return _UnitOfWorkWithTr<T, T>(funcion, r => r);
    }
    /// <summary>
    /// Executes a transactional unit of work that returns an <see cref="EnumerableResult{T}"/>.
    /// The function receives an open connection and a transaction.
    /// Transaction is committed when the result is successful; otherwise it is rolled back.
    /// </summary>
    /// <typeparam name="T">Element type of the enumerable result.</typeparam>
    /// <param name="funcion">Function that performs the operation using the connection and transaction.</param>
    /// <returns>An <see cref="EnumerableResult{T}"/> produced by the function or an exception result.</returns>
    public EnumerableResult<T> UnitOfWork<T>(Func<DbConnection, DbTransaction, EnumerableResult<T>> funcion)
    {
        return _UnitOfWorkWithTr<T,T>(funcion, r => r);
    }
    /// <summary>
    /// Executes a transactional unit of work that returns a <see cref="List{T}"/>.
    /// The function receives an open connection and a transaction. The transaction is committed on success.
    /// </summary>
    /// <typeparam name="T">Element type of the list.</typeparam>
    /// <param name="funcion">Function that performs the operation using the connection and transaction.</param>
    /// <returns>An <see cref="EnumerableResult{T}"/> wrapping the returned list or an exception result.</returns>
    public EnumerableResult<T> UnitOfWork<T>(Func<DbConnection, DbTransaction, IEnumerable<T>> funcion)
    {
        return _UnitOfWorkWithTr<T,T>(funcion, r =>r);
    }

    /// <summary>
    /// Executes an asynchronous transactional unit of work that returns a <see cref="Result{T}"/>.
    /// The transaction is committed when the result is successful; otherwise it is rolled back.
    /// </summary>
    /// <param name="funcion">Function that performs the operation using the connection and transaction.</param>
    /// <param name="cancellationToken">Token used to open the connection, begin the transaction and run the function. The commit is intentionally not cancellable.</param>
    public Task<Result<T>> UnitOfWorkSingleAsync<T>(Func<DbConnection, DbTransaction, Task<Result<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkWithTrResultAsync(funcion, r => r, cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkSingleAsync{T}(Func{DbConnection,DbTransaction,Task{Result{T}}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<Result<T>> UnitOfWorkSingleAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<Result<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkWithTrResultAsync<T, T>(funcion, r => r, cancellationToken);
    }

    /// <summary>
    /// Executes an asynchronous transactional unit of work that returns a plain value wrapped into <see cref="Result{T}"/>.
    /// On success the transaction is committed; on exception it is rolled back.
    /// </summary>
    /// <param name="funcion">Function that performs the operation using the connection and transaction.</param>
    /// <param name="cancellationToken">Token used to open the connection, begin the transaction and run the function. The commit is intentionally not cancellable.</param>
    public Task<Result<T>> UnitOfWorkSingleAsync<T>(Func<DbConnection, DbTransaction, Task<T>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkWithTrValueAsync(funcion, r => r, cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkSingleAsync{T}(Func{DbConnection,DbTransaction,Task{T}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<Result<T>> UnitOfWorkSingleAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<T>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkWithTrValueAsync<T, T>(funcion, r => r, cancellationToken);
    }

    /// <summary>
    /// Executes an asynchronous transactional unit of work that returns an <see cref="EnumerableResult{T}"/>.
    /// The transaction is committed when the result is successful; otherwise it is rolled back.
    /// </summary>
    /// <param name="funcion">Function that performs the operation using the connection and transaction.</param>
    /// <param name="cancellationToken">Token used to open the connection, begin the transaction and run the function. The commit is intentionally not cancellable.</param>
    public Task<EnumerableResult<T>> UnitOfWorkAsync<T>(Func<DbConnection, DbTransaction, Task<EnumerableResult<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkWithTrEnumerableResultAsync(funcion, r => r, cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkAsync{T}(Func{DbConnection,DbTransaction,Task{EnumerableResult{T}}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<EnumerableResult<T>> UnitOfWorkAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<EnumerableResult<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkWithTrEnumerableResultAsync<T, T>(funcion, r => r, cancellationToken);
    }

    /// <summary>
    /// Executes an asynchronous transactional unit of work that returns a sequence wrapped into <see cref="EnumerableResult{T}"/>.
    /// On success the transaction is committed; on exception it is rolled back.
    /// </summary>
    /// <param name="funcion">Function that performs the operation using the connection and transaction.</param>
    /// <param name="cancellationToken">Token used to open the connection, begin the transaction and run the function. The commit is intentionally not cancellable.</param>
    public Task<EnumerableResult<T>> UnitOfWorkAsync<T>(Func<DbConnection, DbTransaction, Task<IEnumerable<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkWithTrEnumerableAsync(funcion, r => r, cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkAsync{T}(Func{DbConnection,DbTransaction,Task{IEnumerable{T}}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<EnumerableResult<T>> UnitOfWorkAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<IEnumerable<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkWithTrEnumerableAsync<T, T>(funcion, r => r, cancellationToken);
    }

    /// <summary>
    /// Executes a unit of work against an open connection and returns a <see cref="Result{T}"/>.
    /// The connection is opened and closed by this method.
    /// </summary>
    /// <typeparam name="T">Type returned by the operation.</typeparam>
    /// <param name="funcion">Function executed with the open connection.</param>
    /// <returns>A <see cref="Result{T}"/> produced by the function or an exception result.</returns>
    public Result<T> UnitOfWorkSingle<T>(Func<DbConnection,Result<T>> funcion)
    {
        return _UnitOfWorkNoTr(funcion, r => r);
    }

    /// <summary>
    /// Executes a unit of work against an open connection and returns a plain value wrapped into <see cref="Result{T}"/>.
    /// The connection is opened and closed by this method.
    /// </summary>
    /// <typeparam name="T">Type returned by the operation.</typeparam>
    /// <param name="funcion">Function executed with the open connection.</param>
    /// <returns>A <see cref="Result{T}"/> wrapping the returned value or an exception result.</returns>
    public Result<T> UnitOfWorkSingle<T>(Func<DbConnection,T?> funcion)
    {
        return _UnitOfWorkNoTr(funcion, r => r);
    }

    /// <summary>
    /// Executes an asynchronous unit of work against an open connection and returns a <see cref="Result{T}"/>.
    /// The connection is opened and closed by this method.
    /// </summary>
    /// <param name="funcion">Function executed with the open connection.</param>
    /// <param name="cancellationToken">Token used to open the connection.</param>
    public Task<Result<T>> UnitOfWorkSingleAsync<T>(Func<DbConnection, Task<Result<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkNoTrResultAsync(funcion, r => r, cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkSingleAsync{T}(Func{DbConnection,Task{Result{T}}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<Result<T>> UnitOfWorkSingleAsync<T>(Func<DbConnection, CancellationToken, Task<Result<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkNoTrResultAsync<T, T>(funcion, r => r, cancellationToken);
    }

    /// <summary>
    /// Executes an asynchronous unit of work against an open connection and returns a plain value wrapped into <see cref="Result{T}"/>.
    /// The connection is opened and closed by this method.
    /// </summary>
    /// <param name="funcion">Function executed with the open connection.</param>
    /// <param name="cancellationToken">Token used to open the connection.</param>
    public Task<Result<T>> UnitOfWorkSingleAsync<T>(Func<DbConnection, Task<T?>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkNoTrValueAsync(funcion, r => r, cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkSingleAsync{T}(Func{DbConnection,Task{T}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<Result<T>> UnitOfWorkSingleAsync<T>(Func<DbConnection, CancellationToken, Task<T?>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkNoTrValueAsync<T, T>(funcion, r => r, cancellationToken);
    }
    /// <summary>
    /// Executes a unit of work against an open connection and returns an <see cref="EnumerableResult{T}"/>.
    /// The connection is opened and closed by this method.
    /// </summary>
    /// <typeparam name="T">Element type of the enumerable result.</typeparam>
    /// <param name="funcion">Function executed with the open connection.</param>
    /// <returns>An <see cref="EnumerableResult{T}"/> produced by the function or an exception result.</returns>
    public EnumerableResult<T> UnitOfWork<T>(Func<DbConnection,EnumerableResult<T>> funcion)
    {
       return _UnitOfWorkNoTr<T,T>(funcion, r => r);
    }
    /// <summary>
    /// Executes a unit of work against an open connection and returns a <see cref="List{T}"/>.
    /// The connection is opened and closed by this method.
    /// </summary>
    /// <typeparam name="T">Element type of the list result.</typeparam>
    /// <param name="funcion">Function executed with the open connection.</param>
    /// <returns>An <see cref="EnumerableResult{T}"/> wrapping the returned list or an exception result.</returns>
    public EnumerableResult<T> UnitOfWork<T>(Func<DbConnection,IEnumerable<T>> funcion)
    {
        return _UnitOfWorkNoTr<T,T>(funcion, r =>r);
    }
    /// <summary>
    /// Executes an asynchronous unit of work against an open connection and returns an <see cref="EnumerableResult{T}"/>.
    /// The connection is opened and closed by this method.
    /// </summary>
    /// <param name="funcion">Function executed with the open connection.</param>
    /// <param name="cancellationToken">Token used to open the connection.</param>
    public Task<EnumerableResult<T>> UnitOfWorkAsync<T>(Func<DbConnection, Task<EnumerableResult<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkNoTrEnumerableResultAsync(funcion, r => r, cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkAsync{T}(Func{DbConnection,Task{EnumerableResult{T}}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<EnumerableResult<T>> UnitOfWorkAsync<T>(Func<DbConnection, CancellationToken, Task<EnumerableResult<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkNoTrEnumerableResultAsync<T, T>(funcion, r => r, cancellationToken);
    }

    /// <summary>
    /// Executes an asynchronous unit of work against an open connection and returns a sequence wrapped into <see cref="EnumerableResult{T}"/>.
    /// The connection is opened and closed by this method.
    /// </summary>
    /// <param name="funcion">Function executed with the open connection.</param>
    /// <param name="cancellationToken">Token used to open the connection.</param>
    public Task<EnumerableResult<T>> UnitOfWorkAsync<T>(Func<DbConnection, Task<IEnumerable<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkNoTrEnumerableAsync(funcion, r => r, cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkAsync{T}(Func{DbConnection,Task{IEnumerable{T}}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<EnumerableResult<T>> UnitOfWorkAsync<T>(Func<DbConnection, CancellationToken, Task<IEnumerable<T>>> funcion, CancellationToken cancellationToken = default)
    {
        return _UnitOfWorkNoTrEnumerableAsync<T, T>(funcion, r => r, cancellationToken);
    }
    /// <summary>
    /// Create a UnitOfWork over a function that returns a bool and is executed in a IDbConnection and a IDbTransaction.
    /// The boolean result is converted to a <see cref="VoidResult"/>.
    /// </summary>
    /// <param name="funcion">Function executed with the connection and transaction.</param>
    /// <returns>A <see cref="VoidResult"/> representing success or failure.</returns>
    public VoidResult UnitOfWorkAction(Func<DbConnection,DbTransaction,bool> funcion)
    {
        return _UnitOfWorkWithTr<VoidResult,bool>(funcion,VoidResult.FromBool);
    }
    /// <summary>
    /// Executes an asynchronous transactional unit of work whose boolean result is converted to a <see cref="VoidResult"/>.
    /// </summary>
    /// <param name="funcion">Function executed with the connection and transaction.</param>
    /// <param name="cancellationToken">Token used to open the connection, begin the transaction and run the function. The commit is intentionally not cancellable.</param>
    public Task<VoidResult> UnitOfWorkActionAsync(Func<DbConnection, DbTransaction, Task<bool>> funcion, CancellationToken cancellationToken = default)
    {
        return UnitOfWorkActionAsync((DbConnection conn, DbTransaction tr, CancellationToken _) => funcion(conn, tr), cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkActionAsync(Func{DbConnection,DbTransaction,Task{bool}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public async Task<VoidResult> UnitOfWorkActionAsync(Func<DbConnection, DbTransaction, CancellationToken, Task<bool>> funcion, CancellationToken cancellationToken = default)
    {
        return await _UnitOfWorkWithTrValueAsync<VoidResult, bool>(funcion, VoidResult.FromBool, cancellationToken);
    }
    /// <summary>
    /// Create a UnitOfWork over a function that returns a bool and is executed in a IDbConnection.
    /// The boolean result is converted to a <see cref="VoidResult"/>.
    /// </summary>
    /// <param name="funcion">Function executed with the connection.</param>
    /// <returns>A <see cref="VoidResult"/> representing success or failure.</returns>
    public VoidResult UnitOfWorkAction(Func<DbConnection,bool> funcion)
    {
        return _UnitOfWorkNoTr(funcion, VoidResult.FromBool);
    }
    /// <summary>
    /// Executes an asynchronous unit of work against an open connection whose boolean result is converted to a <see cref="VoidResult"/>.
    /// </summary>
    /// <param name="funcion">Function executed with the connection.</param>
    /// <param name="cancellationToken">Token used to open the connection.</param>
    public Task<VoidResult> UnitOfWorkActionAsync(Func<DbConnection, Task<bool>> funcion, CancellationToken cancellationToken = default)
    {
        return UnitOfWorkActionAsync((DbConnection conn, CancellationToken _) => funcion(conn), cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkActionAsync(Func{DbConnection,Task{bool}},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public async Task<VoidResult> UnitOfWorkActionAsync(Func<DbConnection, CancellationToken, Task<bool>> funcion, CancellationToken cancellationToken = default)
    {
        return await _UnitOfWorkNoTrValueAsync<VoidResult, bool>(funcion, VoidResult.FromBool, cancellationToken);
    }
    /// <summary>
    /// Create a UnitOfWork over an action that is executed in a IDbConnection and a IDbTransaction.
    /// The action is executed inside a transaction; success is represented by a <see cref="VoidResult"/>.
    /// </summary>
    /// <param name="funcion">Action executed with the connection and transaction.</param>
    /// <returns>A <see cref="VoidResult"/> representing success.</returns>
    public VoidResult UnitOfWorkAction(Action<DbConnection,DbTransaction> funcion)
    {
        return UnitOfWorkAction((c,t) => {
            funcion(c,t);
            return true;
        });
    }
    /// <summary>
    /// Executes an asynchronous transactional action; success is represented by a <see cref="VoidResult"/>.
    /// </summary>
    /// <param name="funcion">Action executed with the connection and transaction.</param>
    /// <param name="cancellationToken">Token used to open the connection, begin the transaction and run the function. The commit is intentionally not cancellable.</param>
    public Task<VoidResult> UnitOfWorkActionAsync(Func<DbConnection, DbTransaction, Task> funcion, CancellationToken cancellationToken = default)
    {
        return UnitOfWorkActionAsync((DbConnection conn, DbTransaction tr, CancellationToken _) => funcion(conn, tr), cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkActionAsync(Func{DbConnection,DbTransaction,Task},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<VoidResult> UnitOfWorkActionAsync(Func<DbConnection, DbTransaction, CancellationToken, Task> funcion, CancellationToken cancellationToken = default)
    {
        return UnitOfWorkActionAsync(async (DbConnection conn, DbTransaction tr, CancellationToken ct) =>
        {
            await funcion(conn, tr, ct);
            return true;
        }, cancellationToken);
    }
    /// <summary>
    /// Create a UnitOfWork over an action that is executed in a IDbConnection.
    /// The action is executed with an open connection and success is represented by a <see cref="VoidResult"/>.
    /// </summary>
    /// <param name="funcion">Action executed with the open connection.</param>
    /// <returns>A <see cref="VoidResult"/> representing success.</returns>
    public VoidResult UnitOfWorkAction(Action<DbConnection> funcion)
    {
        return UnitOfWorkAction(c => {
            funcion(c);
            return true;
        });
    }
    /// <summary>
    /// Executes an asynchronous action against an open connection; success is represented by a <see cref="VoidResult"/>.
    /// </summary>
    /// <param name="funcion">Action executed with the open connection.</param>
    /// <param name="cancellationToken">Token used to open the connection.</param>
    public Task<VoidResult> UnitOfWorkActionAsync(Func<DbConnection, Task> funcion, CancellationToken cancellationToken = default)
    {
        return UnitOfWorkActionAsync((DbConnection conn, CancellationToken _) => funcion(conn), cancellationToken);
    }

    /// <inheritdoc cref="UnitOfWorkActionAsync(Func{DbConnection,Task},CancellationToken)"/>
    /// <remarks>The <paramref name="cancellationToken"/> is also forwarded to <paramref name="funcion"/>.</remarks>
    public Task<VoidResult> UnitOfWorkActionAsync(Func<DbConnection, CancellationToken, Task> funcion, CancellationToken cancellationToken = default)
    {
        return UnitOfWorkActionAsync(async (DbConnection conn, CancellationToken ct) =>
        {
            await funcion(conn, ct);
            return true;
        }, cancellationToken);
    }
    
}