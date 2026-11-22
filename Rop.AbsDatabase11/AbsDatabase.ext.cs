namespace Rop.Database;

public partial class AbsDatabase
{
    // Delete partial-key
    /// <summary>
    /// Deletes rows from the table associated with type <typeparamref name="T"/> using a partial key.
    /// </summary>
    /// <typeparam name="T">Entity type whose table contains the partial key.</typeparam>
    /// <param name="id">Value of the partial key to delete.</param>
    /// <returns>Result with the number of affected rows.</returns>
    public Result<int> DeleteByPartialKey<T>(object id) where T : class
        => UnitOfWorkSingle((conn, tr) => conn.DeleteByPartialKey<T>(id, tr));

    /// <summary>
    /// Deletes rows (async) from the table associated with type <typeparamref name="T"/> using a partial key.
    /// </summary>
    /// <typeparam name="T">Entity type whose table contains the partial key.</typeparam>
    /// <param name="id">Value of the partial key to delete.</param>
    /// <param name="cancellationToken">Cancellation token propagated to the connection and the transaction.</param>
    /// <returns>Async result with the number of affected rows.</returns>
    public Task<Result<int>> DeleteByPartialKeyAsync<T>(object id, CancellationToken cancellationToken = default) where T : class
        => UnitOfWorkSingleAsync((conn, tr) => conn.DeleteByPartialKeyAsync<T>(id, tr), cancellationToken);

    // Insert no key attributes
    /// <summary>
    /// Inserts an entity omitting key attributes (an insert that ignores tagged key attributes).
    /// </summary>
    /// <typeparam name="T">Type of the entity to insert.</typeparam>
    /// <param name="item">Entity to insert.</param>
    /// <returns>Result with the number of affected rows.</returns>
    public Result<int> InsertNoKeyAtt<T>(T item) where T : class
        => UnitOfWorkSingle((conn, tr) => conn.InsertNoKeyAtt<T>(item, tr));

    /// <summary>
    /// Inserts an entity (async) omitting key attributes.
    /// </summary>
    /// <typeparam name="T">Type of the entity to insert.</typeparam>
    /// <param name="item">Entity to insert.</param>
    /// <param name="cancellationToken">Cancellation token propagated to the connection and the transaction.</param>
    /// <returns>Async result with the number of affected rows.</returns>
    public Task<Result<int>> InsertNoKeyAttAsync<T>(T item, CancellationToken cancellationToken = default) where T : class
        => UnitOfWorkSingleAsync<int>((conn, tr) => conn.InsertNoKeyAttAsync<T>(item, tr), cancellationToken);

    // Insert or update with MERGE
    /// <summary>
    /// Inserts or updates an entity using the MERGE strategy defined in the extensions.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="item">Entity to insert or update.</param>
    /// <returns>Result with the key or number of affected rows depending on the implementation.</returns>
    public Result<int> InsertOrUpdateMerge<T>(T item) where T : class
        => UnitOfWorkSingle((conn, tr) => conn.InsertOrUpdateMerge<T>(item, tr));

    /// <summary>
    /// Inserts or updates an entity using MERGE (async).
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="item">Entity to insert or update.</param>
    /// <param name="cancellationToken">Cancellation token propagated to the connection and the transaction.</param>
    /// <returns>Async result with the key or number of affected rows depending on the implementation.</returns>
    public Task<Result<int>> InsertOrUpdateMergeAsync<T>(T item, CancellationToken cancellationToken = default) where T : class
        => UnitOfWorkSingleAsync<int>((conn, tr) => conn.InsertOrUpdateMergeAsync<T>(item, tr), cancellationToken);

    // InsertOrUpdatePartial
    /// <summary>
    /// Replaces (deletes and re-inserts) the rows related to the partial key <paramref name="id"/>
    /// and then inserts the list of <paramref name="items"/> within a transaction.
    /// </summary>
    /// <typeparam name="T">Type of the partial entities.</typeparam>
    /// <param name="id">Value of the partial key to replace.</param>
    /// <param name="items">List of entities to insert.</param>
    /// <returns>Result with true if all inserts were performed successfully.</returns>
    public Result<bool> InsertOrUpdatePartial<T>(object id, IReadOnlyList<T> items) where T : class
        => UnitOfWorkSingle((conn, tr) => conn.InsertOrUpdatePartial<T>(id, items, tr));

    /// <summary>
    /// Replaces the rows related to the partial key and re-inserts the <paramref name="items"/> (async).
    /// </summary>
    /// <typeparam name="T">Type of the partial entities.</typeparam>
    /// <param name="id">Value of the partial key to replace.</param>
    /// <param name="items">List of entities to insert.</param>
    /// <param name="cancellationToken">Cancellation token propagated to the connection and the transaction.</param>
    /// <returns>Async result with true if all inserts were performed successfully.</returns>
    public Task<Result<bool>> InsertOrUpdatePartialAsync<T>(object id, IReadOnlyList<T> items, CancellationToken cancellationToken = default) where T : class
        => UnitOfWorkSingleAsync<bool>(async (conn, tr) =>
        {
            var v = await  conn.InsertOrUpdatePartialAsync<T>(id, items, tr);
            return v;
        }, cancellationToken);

    // UpdateIdValue async
    /// <summary>
    /// Updates the value of a field for the row with key <paramref name="id"/> (async).
    /// </summary>
    /// <typeparam name="DB">Type describing the target table (used to resolve metadata).</typeparam>
    /// <typeparam name="T">Type of the value to assign.</typeparam>
    /// <param name="id">Value of the row key to update.</param>
    /// <param name="value">New value to set.</param>
    /// <param name="field">Name of the field to update.</param>
    /// <param name="cancellationToken">Cancellation token propagated to the connection and the transaction.</param>
    /// <returns>Async result with true if exactly one row was updated.</returns>
    public Task<Result<bool>> UpdateIdValueAsync<DB, T>(object id, T value, string field, CancellationToken cancellationToken = default)
        => UnitOfWorkSingleAsync<bool>((conn, tr) => conn.UpdateIdValueAsync<DB, T>(id, value, field, tr), cancellationToken);

    // GetPartial / GetPartial2
    /// <summary>
    /// Gets a partial entity identified by two keys (id1, id2).
    /// </summary>
    /// <typeparam name="T">Type of the entity to retrieve.</typeparam>
    /// <param name="id1">Value of the first key.</param>
    /// <param name="id2">Value of the second key.</param>
    /// <returns>Result with the entity if it exists, or null otherwise.</returns>
    public Result<T> GetPartial<T>(object id1, object id2) where T : class
        => UnitOfWorkSingle(conn => conn.GetPartial<T>(id1, id2));

    /// <summary>
    /// Gets a partial entity identified by two keys (id1, id2) (async).
    /// </summary>
    /// <typeparam name="T">Type of the entity to retrieve.</typeparam>
    /// <param name="id1">Value of the first key.</param>
    /// <param name="id2">Value of the second key.</param>
    /// <param name="cancellationToken">Cancellation token propagated to the connection and the transaction.</param>
    /// <returns>Async result with the entity if it exists, or null otherwise.</returns>
    public Task<Result<T>> GetPartialAsync<T>(object id1, object id2, CancellationToken cancellationToken = default) where T : class
        => UnitOfWorkSingleAsync(conn => conn.GetPartialAsync<T>(id1, id2), cancellationToken);

    /// <summary>
    /// Gets multiple partial entities matching the key <paramref name="id"/>.
    /// </summary>
    /// <typeparam name="T">Type of the entities to retrieve.</typeparam>
    /// <param name="id">Value of the partial key.</param>
    /// <returns>Enumeration of the entities found.</returns>
    public EnumerableResult<T> GetPartial<T>(object id) where T : class
        => UnitOfWork(conn => conn.GetPartial<T>(id).ToList());

    /// <summary>
    /// Gets multiple partial entities matching the key <paramref name="id"/> (async).
    /// </summary>
    /// <typeparam name="T">Type of the entities to retrieve.</typeparam>
    /// <param name="id">Value of the partial key.</param>
    /// <param name="cancellationToken">Cancellation token propagated to the connection and the transaction.</param>
    /// <returns>Async enumeration of the entities found.</returns>
    public Task<EnumerableResult<T>> GetPartialAsync<T>(object id, CancellationToken cancellationToken = default) where T : class
        => UnitOfWorkAsync(conn => conn.GetPartialAsync<T>(id), cancellationToken);

    /// <summary>
    /// Gets multiple partial entities matching the second partial key.
    /// </summary>
    /// <typeparam name="T">Type of the entities to retrieve.</typeparam>
    /// <param name="id">Value of the second partial key.</param>
    /// <returns>Enumeration of the entities found.</returns>
    public EnumerableResult<T> GetPartial2<T>(object id) where T : class
        => UnitOfWork(conn => conn.GetPartial2<T>(id).ToList());

    /// <summary>
    /// Gets multiple partial entities matching the second partial key (async).
    /// </summary>
    /// <typeparam name="T">Type of the entities to retrieve.</typeparam>
    /// <param name="id">Value of the second partial key.</param>
    /// <param name="cancellationToken">Cancellation token propagated to the connection and the transaction.</param>
    /// <returns>Async enumeration of the entities found.</returns>
    public Task<EnumerableResult<T>> GetPartial2Async<T>(object id, CancellationToken cancellationToken = default) where T : class
        => UnitOfWorkAsync(conn => conn.GetPartial2Async<T>(id), cancellationToken);
}
