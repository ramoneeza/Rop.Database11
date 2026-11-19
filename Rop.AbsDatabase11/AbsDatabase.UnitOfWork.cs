using System.Data.Common;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Rop.Database;


public partial class AbsDatabase
{

    // ===== UnitOfWork síncrono: 8 combinaciones =====
    // Entradas: Solo Conn / Conn + Tr
    // Salidas de la función: Result<T> / EnumerableResult<T> / IEnumerable<T> / T?
    // (IEnumerable<T> y T? se envuelven para poder devolver el error y hacer rollback)
    //
    // Solo Conn:
    //  1. Conn -> Result<T>
    //  2. Conn -> EnumerableResult<T>
    //  3. Conn -> IEnumerable<T>
    //  4. Conn -> T?
    // Conn + Tr:
    //  5. (Conn, Tr) -> Result<T>
    //  6. (Conn, Tr) -> EnumerableResult<T>
    //  7. (Conn, Tr) -> IEnumerable<T>
    //  8. (Conn, Tr) -> T?



    // Normaliza la cancelación del token del llamador.
    //
    // La cancelación se propaga como EXCEPCIÓN (OperationCanceledException) y no como Error:
    // es una señal de flujo de control, no un fallo de negocio. El llamador la distingue
    // de los errores porque la excepción lleva asociado el token (e.CancellationToken).
    //
    // Los proveedores (p. ej. SqlClient) pueden manifestar la cancelación como SqlException
    // en lugar de OperationCanceledException, por lo que aquí se normaliza a una única forma:
    //
    // - Si la excepción YA es OperationCanceledException, se relanza tal cual mediante
    //   ExceptionDispatchInfo.Throw para conservar su stack trace original.
    //   (No se puede usar `throw;` porque este método es un helper y no está dentro del catch.)
    // - Si es cualquier otra excepción (p. ej. una SqlException provocada por la cancelación),
    //   se envuelve en una nueva OperationCanceledException con la excepción como inner
    //   y el token asociado.
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
    // AB-8: rollback "silencioso" cuando se va a reintentar la operación con una
    // conexión/transacción nuevas: el fallo del rollback no debe impedir el reintento.
    private static void _silentRollBack(DbTransaction tr)
    {
        try { tr.Rollback(); } catch { /* se ignora: la conexión está condenada y se reintenta con una nueva */ }
    }
    private static async Task _silentRollBackAsync(DbTransaction tr)
    {
        try { await tr.RollbackAsync(); } catch { /* se ignora: la conexión está condenada y se reintenta con una nueva */ }
    }

    // AB-8: los _catch convierten las excepciones en Result fallido, PERO relanzan
    // las transitorias para que el core de UnitOfWork decida si reintenta
    // (con el número de intento) o desiste. Así la clasificación transitoria
    // no se pierde en el envoltorio.
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

    // ===== Cores síncronos (lógica transaccional, una sola vez) =====
    // TR puede ser Result<R> o EnumerableResult<R> (ambos heredan de AbsResult).

    private TR _UnitOfWorkWithTrCore<TR>(Func<DbConnection, DbTransaction, TR> funcion, Func<Error, TR> onFailed) where TR : AbsResult
    {
        // AB-8: reintento ante errores transitorios. Cada intento usa conexión y
        // transacción NUEVAS; un Result fallido (error de negocio) no se reintenta.
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
        // AB-8: reintento ante errores transitorios (lecturas; los writes internos
        // van por la ruta transaccional). Cada intento usa una conexión NUEVA.
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

    // ===== Las 8 combinaciones síncronas (adaptadores finos sobre los cores) =====

    // 5. (Conn, Tr) -> Result<T>
    private Result<R> _UnitOfWorkWithTr<R, T>(Func<DbConnection, DbTransaction, Result<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkWithTrCore((conn, tr) => funcion(conn, tr).Map(map), e => e);
    }

    // 8. (Conn, Tr) -> T  → envuelto en Result<T> para poder hacer rollback en caso de error
    private Result<R> _UnitOfWorkWithTr<R, T>(Func<DbConnection, DbTransaction, T> funcion, Func<T, R> map)
    {
        return _UnitOfWorkWithTrCore((conn, tr) => _catch(conn, tr, funcion).Map(map), e => e);
    }

    // 6. (Conn, Tr) -> EnumerableResult<T>
    private EnumerableResult<R> _UnitOfWorkWithTr<R, T>(Func<DbConnection, DbTransaction, EnumerableResult<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkWithTrCore((conn, tr) => funcion(conn, tr).Map(map), e => e);
    }

    // 7. (Conn, Tr) -> IEnumerable<T>  → envuelto en EnumerableResult<T> para poder hacer rollback en caso de error
    private EnumerableResult<R> _UnitOfWorkWithTr<R, T>(Func<DbConnection, DbTransaction, IEnumerable<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkWithTrCore((conn, tr) => _catchEnumerable(conn, tr, funcion).Map(map), e => e);
    }

    // 1. Conn -> Result<T>
    private Result<R> _UnitOfWorkNoTr<R, T>(Func<DbConnection, Result<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkNoTrCore(conn => funcion(conn).Map(map), e => e);
    }

    // 4. Conn -> T?  → envuelto en Result<T> para poder devolver el error en caso de excepción
    private Result<R> _UnitOfWorkNoTr<R, T>(Func<DbConnection, T?> funcion, Func<T, R> map)
    {
        return _UnitOfWorkNoTrCore(conn => _catch(conn, funcion).Map(map), e => e);
    }

    // 2. Conn -> EnumerableResult<T>
    private EnumerableResult<R> _UnitOfWorkNoTr<R, T>(Func<DbConnection, EnumerableResult<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkNoTrCore(conn => funcion(conn).Map(map), e => e);
    }

    // 3. Conn -> IEnumerable<T>  → envuelto en EnumerableResult<T> para poder devolver el error en caso de excepción
    private EnumerableResult<R> _UnitOfWorkNoTr<R, T>(Func<DbConnection, IEnumerable<T>> funcion, Func<T, R> map)
    {
        return _UnitOfWorkNoTrCore(conn => _catchEnumerable(conn, funcion).Map(map), e => e);
    }

    // ===== Async: 16 combinaciones =====
    // Entradas: Solo Conn / Conn + Tr
    // Salidas de la función: Result<T> / EnumerableResult<T> / IEnumerable<T> / T?
    // La función puede aceptar o no CancellationToken (2 variantes).
    //
    // Solo Conn:
    //  1. Conn -> Result<T>                       (sin ct)
    //  2. Conn -> Result<T>                       (con ct)
    //  3. Conn -> EnumerableResult<T>             (sin ct)
    //  4. Conn -> EnumerableResult<T>             (con ct)
    //  5. Conn -> IEnumerable<T>                  (sin ct)
    //  6. Conn -> IEnumerable<T>                  (con ct)
    //  7. Conn -> T?                              (sin ct)
    //  8. Conn -> T?                              (con ct)
    // Conn + Tr:
    //  9.  (Conn, Tr) -> Result<T>                (sin ct)
    // 10.  (Conn, Tr) -> Result<T>                (con ct)
    // 11.  (Conn, Tr) -> EnumerableResult<T>      (sin ct)
    // 12.  (Conn, Tr) -> EnumerableResult<T>      (con ct)
    // 13.  (Conn, Tr) -> IEnumerable<T>           (sin ct)
    // 14.  (Conn, Tr) -> IEnumerable<T>           (con ct)
    // 15.  (Conn, Tr) -> T?                       (sin ct)
    // 16.  (Conn, Tr) -> T?                       (con ct)
    //
    // Decisiones: el rollback y el commit se ejecutan sin token (el rollback no debe
    // quedarse a medias; el commit no debe dejar el resultado indeterminado).

    // ===== Cores asíncronos (lógica transaccional, una sola vez) =====
    // TR puede ser Result<R> o EnumerableResult<R> (ambos heredan de AbsResult).

    private async Task<TR> _UnitOfWorkWithTrCoreAsync<TR>(Func<DbConnection, DbTransaction, CancellationToken, Task<TR>> funcion, Func<Error, TR> onFailed, CancellationToken ct) where TR : AbsResult
    {
        // AB-8: reintento ante errores transitorios. Cada intento usa conexión y
        // transacción NUEVAS; un Result fallido (error de negocio) no se reintenta.
        // La cancelación tiene prioridad sobre el reintento.
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
                        // Commit sin token: si se cancelara durante el commit el resultado quedaría indeterminado.
                        await tr.CommitAsync();
                        return res;
                    }
                    catch (Exception ex) when (ct.IsCancellationRequested)
                    {
                        try { await tr.RollbackAsync(); }
                        catch (Exception rbex) { Debug.WriteLine($"Rollback fallido tras cancelación: {rbex.Message}"); }
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

    // ===== Adaptadores WithTr (8) =====

    // 9. (Conn, Tr) -> Result<T> (sin ct)
    private Task<Result<R>> _UnitOfWorkWithTrResultAsync<R, T>(Func<DbConnection, DbTransaction, Task<Result<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, _) => (await funcion(conn, tr)).Map(map), e => e, ct);
    }

    // 10. (Conn, Tr) -> Result<T> (con ct)
    private Task<Result<R>> _UnitOfWorkWithTrResultAsync<R, T>(Func<DbConnection, DbTransaction, CancellationToken, Task<Result<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await funcion(conn, tr, c)).Map(map), e => e, ct);
    }

    // 15. (Conn, Tr) -> T? (sin ct)
    private Task<Result<R>> _UnitOfWorkWithTrValueAsync<R, T>(Func<DbConnection, DbTransaction, Task<T>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await _catchAsync(conn, tr, c, (conn2, tr2, _) => funcion(conn2, tr2))).Map(map), e => e, ct);
    }

    // 16. (Conn, Tr) -> T? (con ct)
    private Task<Result<R>> _UnitOfWorkWithTrValueAsync<R, T>(Func<DbConnection, DbTransaction, CancellationToken, Task<T>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await _catchAsync(conn, tr, c, funcion)).Map(map), e => e, ct);
    }

    // 11. (Conn, Tr) -> EnumerableResult<T> (sin ct)
    private Task<EnumerableResult<R>> _UnitOfWorkWithTrEnumerableResultAsync<R, T>(Func<DbConnection, DbTransaction, Task<EnumerableResult<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, _) => (await funcion(conn, tr)).Map(map), e => e, ct);
    }

    // 12. (Conn, Tr) -> EnumerableResult<T> (con ct)
    private Task<EnumerableResult<R>> _UnitOfWorkWithTrEnumerableResultAsync<R, T>(Func<DbConnection, DbTransaction, CancellationToken, Task<EnumerableResult<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await funcion(conn, tr, c)).Map(map), e => e, ct);
    }

    // 13. (Conn, Tr) -> IEnumerable<T> (sin ct)
    private Task<EnumerableResult<R>> _UnitOfWorkWithTrEnumerableAsync<R, T>(Func<DbConnection, DbTransaction, Task<IEnumerable<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await _catchEnumerableAsync(conn, tr, c, (conn2, tr2, _) => funcion(conn2, tr2))).Map(map), e => e, ct);
    }

    // 14. (Conn, Tr) -> IEnumerable<T> (con ct)
    private Task<EnumerableResult<R>> _UnitOfWorkWithTrEnumerableAsync<R, T>(Func<DbConnection, DbTransaction, CancellationToken, Task<IEnumerable<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkWithTrCoreAsync(async (conn, tr, c) => (await _catchEnumerableAsync(conn, tr, c, funcion)).Map(map), e => e, ct);
    }

    private async Task<TR> _UnitOfWorkNoTrCoreAsync<TR>(Func<DbConnection, CancellationToken, Task<TR>> funcion, Func<Error, TR> onFailed, CancellationToken ct) where TR : AbsResult
    {
        // AB-8: reintento ante errores transitorios (lecturas; los writes internos
        // van por la ruta transaccional). Cada intento usa una conexión NUEVA.
        // La cancelación tiene prioridad sobre el reintento.
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

    // ===== Adaptadores NoTr (8) =====

    // 1. Conn -> Result<T> (sin ct)
    private Task<Result<R>> _UnitOfWorkNoTrResultAsync<R, T>(Func<DbConnection, Task<Result<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, _) => (await funcion(conn)).Map(map), e => e, ct);
    }

    // 2. Conn -> Result<T> (con ct)
    private Task<Result<R>> _UnitOfWorkNoTrResultAsync<R, T>(Func<DbConnection, CancellationToken, Task<Result<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await funcion(conn, c)).Map(map), e => e, ct);
    }

    // 7. Conn -> T? (sin ct)
    private Task<Result<R>> _UnitOfWorkNoTrValueAsync<R, T>(Func<DbConnection, Task<T?>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await _catchAsync(conn, c, (conn2, _) => funcion(conn2))).Map(map), e => e, ct);
    }

    // 8. Conn -> T? (con ct)
    private Task<Result<R>> _UnitOfWorkNoTrValueAsync<R, T>(Func<DbConnection, CancellationToken, Task<T?>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await _catchAsync(conn, c, funcion)).Map(map), e => e, ct);
    }

    // 3. Conn -> EnumerableResult<T> (sin ct)
    private Task<EnumerableResult<R>> _UnitOfWorkNoTrEnumerableResultAsync<R, T>(Func<DbConnection, Task<EnumerableResult<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, _) => (await funcion(conn)).Map(map), e => e, ct);
    }

    // 4. Conn -> EnumerableResult<T> (con ct)
    private Task<EnumerableResult<R>> _UnitOfWorkNoTrEnumerableResultAsync<R, T>(Func<DbConnection, CancellationToken, Task<EnumerableResult<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await funcion(conn, c)).Map(map), e => e, ct);
    }

    // 5. Conn -> IEnumerable<T> (sin ct)
    private Task<EnumerableResult<R>> _UnitOfWorkNoTrEnumerableAsync<R, T>(Func<DbConnection, Task<IEnumerable<T>>> funcion, Func<T, R> map, CancellationToken ct)
    {
        return _UnitOfWorkNoTrCoreAsync(async (conn, c) => (await _catchEnumerableAsync(conn, c, (conn2, _) => funcion(conn2))).Map(map), e => e, ct);
    }

    // 6. Conn -> IEnumerable<T> (con ct)
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