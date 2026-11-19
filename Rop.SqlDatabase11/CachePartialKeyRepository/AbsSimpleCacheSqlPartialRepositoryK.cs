using System.Reflection;
using Rop.Database11.CacheRepository;
using Rop.Database11.PartialKeyRepository;

namespace Rop.Database11.CachePartialKeyRepository
{
    /// <summary>
    /// Base class for a simple SQL repository with partial key support, DTO and cache
    /// expiration, for tables or databases WITHOUT change tracking.
    /// </summary>
    /// <typeparam name="T">Entity type (business model).</typeparam>
    /// <typeparam name="D">DTO type (database table representation).</typeparam>
    /// <typeparam name="K">Partial key type.</typeparam>
    /// <remarks>
    /// Groups the items by their first partial key into <see cref="PartialBlock{K,T}"/>
    /// blocks (a list per key) and caches them with TTL expiration: the cache is the
    /// invalidation mechanism that substitutes change tracking.
    /// </remarks>
    public abstract class AbsSimpleCacheSqlPartialRepositoryK<T, D, K>
        : AbsSimpleCacheSqlRepositoryK<PartialBlock<K, T>, PartialBlock<K, D>, K>
        where T : class where D : class where K : notnull
    {
        /// <summary>
        /// Gets the partial key description for the repository.
        /// </summary>
        /// <returns>The partial key description.</returns>
        public override PartialKeyDescription KeyDescription => (PartialKeyDescription)base.KeyDescription;

        /// <summary>
        /// Gets the partial key of the given DTO item.
        /// </summary>
        /// <param name="item">DTO instance.</param>
        /// <returns>Partial key of the item.</returns>
        public virtual K GetKey(D item) => (K)KeyDescription.GetKeyValue(item);

        /// <summary>
        /// Gets the key of the given partial block.
        /// </summary>
        /// <param name="item">Partial block instance.</param>
        /// <returns>Key of the block.</returns>
        public override K GetTKey(PartialBlock<K, T> item) => item.Key;

        /// <summary>
        /// Gets the key of the given DTO partial block.
        /// </summary>
        /// <param name="item">DTO partial block instance.</param>
        /// <returns>Key of the block.</returns>
        public override K GetDKey(PartialBlock<K, D> item) => item.Key;

        /// <summary>
        /// Maps a DTO item to the entity type.
        /// </summary>
        /// <param name="item">DTO instance.</param>
        /// <returns>Mapped entity.</returns>
        protected abstract T Map(D item);

        /// <summary>
        /// Maps a DTO block to an entity block.
        /// </summary>
        /// <param name="item">DTO partial block instance.</param>
        /// <returns>Mapped entity block.</returns>
        protected override PartialBlock<K, T> Map(PartialBlock<K, D> item)
        {
            var lst = item.Block.Select(Map);
            return new PartialBlock<K, T>(item.Key, lst);
        }

        /// <summary>
        /// Reloads all DTO items from the database.
        /// </summary>
        /// <returns>Result with the loaded DTOs or the error.</returns>
        protected abstract EnumerableResult<D> IntReloadAllPartial();

        /// <summary>
        /// Reloads all items from the database, grouping them into partial blocks.
        /// </summary>
        /// <returns>Result with the loaded partial blocks or the error.</returns>
        protected override EnumerableResult<PartialBlock<K, T>> IntReloadAll()
        {
            var rall = IntReloadAllPartial();
            if (rall.IsFailed) return rall.Error!;
            var gr = rall.Value.GroupBy(GetKey).Select(g => Map(new PartialBlock<K, D>(g.Key, g.ToList())));
            return gr.ToList();
        }

        /// <summary>
        /// Reloads the DTO items with the given keys from the database.
        /// </summary>
        /// <param name="keys">Keys to reload.</param>
        /// <returns>Result with the loaded DTOs or the error.</returns>
        protected abstract EnumerableResult<D> IntReloadSomePartial(params IEnumerable<K> keys);

        /// <summary>
        /// Reloads the items with the given keys from the database, grouping them into partial blocks.
        /// </summary>
        /// <param name="keys">Keys to reload.</param>
        /// <returns>Result with the loaded partial blocks or the error.</returns>
        protected override EnumerableResult<PartialBlock<K, T>> IntReloadSome(params IEnumerable<K> keys)
        {
            var rall = IntReloadSomePartial(keys);
            if (rall.IsFailed) return rall.Error!;
            var gr = rall.Value.GroupBy(GetKey).Select(g => Map(new PartialBlock<K, D>(g.Key, g.ToList())));
            return gr.ToList();
        }

        /// <summary>
        /// The entity type of this repository is <see cref="PartialBlock{K,T}"/>, whose key
        /// is exposed by the <see cref="PartialBlock{K,T}.Key"/> property (not by the DTO key property).
        /// </summary>
        /// <param name="keydescription">Key description of the DTO type.</param>
        /// <returns>The <c>Key</c> property of the partial block type.</returns>
        protected override PropertyInfo? FindKeyPropT(KeyDescription keydescription)
            => typeof(PartialBlock<K, T>).GetProperty(nameof(PartialBlock<K, T>.Key));

        // Constructor
        /// <summary>
        /// Initializes the repository using the partial key description derived from the DTO type.
        /// </summary>
        /// <param name="database">Database instance.</param>
        /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
        /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
        protected AbsSimpleCacheSqlPartialRepositoryK(Database database, TimeSpan? defaultExpirationTime = null, TimeSpan? defaultcleantime = null)
            : base(database, DapperHelperExtend.GetPartialKeyDescription(typeof(D)), defaultExpirationTime, defaultcleantime)
        {
        }
    }

    /// <summary>
    /// Base class for a simple SQL repository with partial key support, cache expiration,
    /// key of type int and DTO.
    /// </summary>
    /// <typeparam name="T">Entity type (business model).</typeparam>
    /// <typeparam name="D">DTO type (database table representation).</typeparam>
    /// <param name="database">Database instance.</param>
    /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
    /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
    public abstract class AbsSimpleCacheSqlPartialIntRepository<T, D>(Database database, TimeSpan? defaultExpirationTime = null, TimeSpan? defaultcleantime = null)
        : AbsSimpleCacheSqlPartialRepositoryK<T, D, int>(database, defaultExpirationTime, defaultcleantime)
        where T : class
        where D : class
    {
    }

    /// <summary>
    /// Base class for a simple SQL repository with partial key support, cache expiration,
    /// key of type string and DTO.
    /// </summary>
    /// <typeparam name="T">Entity type (business model).</typeparam>
    /// <typeparam name="D">DTO type (database table representation).</typeparam>
    /// <param name="database">Database instance.</param>
    /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
    /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
    public abstract class AbsSimpleCacheSqlPartialRepository<T, D>(Database database, TimeSpan? defaultExpirationTime = null, TimeSpan? defaultcleantime = null)
        : AbsSimpleCacheSqlPartialRepositoryK<T, D, string>(database, defaultExpirationTime, defaultcleantime)
        where T : class
        where D : class
    {
    }

    /// <summary>
    /// Base class for a simple SQL repository with partial key support, cache expiration,
    /// without specific DTO and key of type string.
    /// </summary>
    /// <typeparam name="T">Entity type, used both as entity and DTO.</typeparam>
    /// <param name="database">Database instance.</param>
    /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
    /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
    public abstract class AbsSimpleCacheSqlPartialRepository<T>(Database database, TimeSpan? defaultExpirationTime = null, TimeSpan? defaultcleantime = null)
        : AbsSimpleCacheSqlPartialRepository<T, T>(database, defaultExpirationTime, defaultcleantime)
        where T : class
    {
        /// <summary>
        /// Maps an entity to itself (identity mapping).
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>The same entity.</returns>
        protected override T Map(T item) => item;
    }

    /// <summary>
    /// Base class for a simple SQL repository with partial key support, cache expiration,
    /// without specific DTO and key of type int.
    /// </summary>
    /// <typeparam name="T">Entity type, used both as entity and DTO.</typeparam>
    /// <param name="database">Database instance.</param>
    /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
    /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
    public abstract class AbsSimpleCacheSqlPartialIntRepository<T>(Database database, TimeSpan? defaultExpirationTime = null, TimeSpan? defaultcleantime = null)
        : AbsSimpleCacheSqlPartialIntRepository<T, T>(database, defaultExpirationTime, defaultcleantime)
        where T : class
    {
        /// <summary>
        /// Maps an entity to itself (identity mapping).
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>The same entity.</returns>
        protected override T Map(T item) => item;
    }
}
