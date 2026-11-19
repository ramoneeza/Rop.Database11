using Rop.Database11.Tracking;

namespace Rop.Database11.CacheRepository
{
    /// <summary>
    /// Base class for a repository with cache that expires after a specified time with DTO and sql table dependency
    /// </summary>
    /// <typeparam name="T">Entity type (business model)</typeparam>
    /// <typeparam name="D">DTO type (database table representation)</typeparam>
    /// <typeparam name="K">Key type</typeparam>
    public abstract class AbsCacheSqlRepositoryK<T,D, K> : AbsSimpleCacheSqlRepositoryK<T,D, K> where T : class where D:class where K : notnull
    {
        /// <summary>
        /// Indicates whether slim (optimized) queries are used to load data.
        /// </summary>
        public bool UseSlim { get; } = false;
        /// <summary>
        /// SQL Server change tracking dependency that notifies of table changes.
        /// </summary>
        public SqlTableDependency TableDependency { get; private set; }
        /// <summary>
        /// Priority of the change tracking polling.
        /// </summary>
        public ChangeTrackingPriority ChangesPriority { get; }

        /// <summary>
        /// Creates the change tracking dependency for the DTO table.
        /// </summary>
        /// <returns>Change tracking dependency.</returns>
        protected virtual SqlTableDependency FactoryTableDependency()
        {
            var td=Database.GetTableDependency(typeof(D), ChangesPriority);
            return td;
        }
        
        private void TableDependency_OnChanged(object? sender,DeltaChanges e)
        {
            ResetIds(e.GetKeys<K>());
        }
        
        /// <summary>
        /// Constructor for cached repository with Change Tracking
        /// </summary>
        /// <param name="database">Database instance</param>
        /// <param name="changesPriority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
        /// <param name="useslim">Use optimized slim queries (default: false)</param>
        /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes)</param>
        protected AbsCacheSqlRepositoryK(Database database, ChangeTrackingPriority? changesPriority = null, bool useslim = false, TimeSpan? defaultExpirationTime = null) : base(database,defaultExpirationTime)
        {
            UseSlim = useslim;
            ChangesPriority = changesPriority ?? ChangeTrackingPriority.Default;
            // ReSharper disable once VirtualMemberCallInConstructor
            TableDependency = FactoryTableDependency();
            TableDependency.OnChanged += TableDependency_OnChanged;
        }
        
        /// <summary>
        /// Loads the entities for the given keys from the database using the configured query mode.
        /// </summary>
        /// <param name="keys">Keys to load.</param>
        /// <returns>Result with the loaded entities, or the error that occurred.</returns>
        protected override EnumerableResult<T> IntReloadSome(IEnumerable<K> keys)
        {
            var alldto = UseSlim ? Database.GetSomeSlim<K, D>(keys) : Database.GetSome<K, D>(keys);
            return alldto.Map(Map);
        }

        // SD-17: al disponer el repo se desuscribe del change tracking (la
        // dependencia y la BD son compartidas) y se dispone el cache.
        /// <summary>
        /// Disposable pattern hook: unsubscribes from change tracking and disposes the cache.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="IDisposable.Dispose"/>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TableDependency.OnChanged -= TableDependency_OnChanged;
            }
            base.Dispose(disposing);
        }
    }
    
    /// <summary>
    /// Base class for a repository with cache that expires after a specified time without specific DTO and sql table dependency
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <typeparam name="K">Key type</typeparam>
    public abstract class AbsCacheSqlRepository<T,K> : AbsCacheSqlRepositoryK<T, T, K> where T : class where K: notnull
    {
        /// <summary>
        /// Maps a DTO instance to its entity representation (identity mapping in this case).
        /// </summary>
        /// <param name="item">DTO instance loaded from the database.</param>
        /// <returns>Entity instance.</returns>
        protected override T Map(T item) =>item;
        
        /// <summary>
        /// Constructor for cached repository with Change Tracking
        /// </summary>
        /// <param name="database">Database instance</param>
        /// <param name="changesPriority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
        /// <param name="useslim">Use optimized slim queries (default: false)</param>
        /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes)</param>
        protected AbsCacheSqlRepository(Database database, ChangeTrackingPriority? changesPriority = null, bool useslim = false, TimeSpan? defaultExpirationTime = null) : base(database, changesPriority, useslim, defaultExpirationTime)
        {
        }
    }
}
