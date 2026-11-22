namespace Rop.Database11.Repository
{
    /// <summary>
    /// Extension indexers for the repositories (EI-1): lets you read entities with
    /// dictionary syntax (<c>repo[key]</c>) instead of <c>repo.Get(key, out _)</c>.
    /// </summary>
    /// <remarks>
    /// The extension indexers do not modify the interface (no implementer breaks)
    /// and coexist with the <see cref="IAbsSimpleSqlRepository{T,K}.Get"/> method.
    /// The operation error remains available in <see cref="IAbsSimpleSqlRepository.LastError"/>.
    /// </remarks>
    public static class RepositoryIndexers
    {
        /// <summary>
        /// Read indexer by key over any simple repository (and its derivatives:
        /// change tracking, cache and partial repositories).
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <typeparam name="K">Key type.</typeparam>
        extension<T, K>(IAbsSimpleSqlRepository<T, K> repo) where T : class where K : notnull
        {
            /// <summary>
            /// Gets the item with the given key from the repository.
            /// </summary>
            public T? this[K key] => repo.Get(key, out _);
        }
    }
}
