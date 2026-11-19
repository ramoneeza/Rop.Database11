namespace Rop.Database11.Repository
{
    /// <summary>
    /// Extension indexers para los repositorios (EI-1): permite leer entidades con
    /// sintaxis de diccionario (<c>repo[key]</c>) en lugar de <c>repo.Get(key, out _)</c>.
    /// </summary>
    /// <remarks>
    /// Los extension indexers no modifican la interfaz (ningún implementador se rompe)
    /// y conviven con el método <see cref="IAbsSimpleSqlRepository{T,K}.Get"/>.
    /// El error de la operación queda disponible en <see cref="IAbsSimpleSqlRepository.LastError"/>.
    /// </remarks>
    public static class RepositoryIndexers
    {
        /// <summary>
        /// Indexer de lectura por clave sobre cualquier repositorio simple (y sus
        /// derivados: repositorios con change tracking, cache y parciales).
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
