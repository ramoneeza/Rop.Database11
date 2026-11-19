using Rop.Database11.Tracking;

namespace Rop.Database11.Repository
{
    /// <summary>
    /// Interface for a SQL repository with table dependency and versioning.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <typeparam name="K">Key type</typeparam>
    public interface ISqlRepository<T,K>:IAbsSimpleSqlRepository<T,K> where T : class where K : notnull
    {
        /// <summary>
        /// Gets the table dependency used for change tracking.
        /// </summary>
        /// <returns>The table dependency.</returns>
        SqlTableDependency TableDependency { get; }
        /// <summary>
        /// Gets the current change tracking version of the table.
        /// </summary>
        /// <returns>The tracking version.</returns>
        TrackingVersion Version { get; }
    }
    
    /// <summary>
    /// Base class for SQL repositories with table dependency and versioning with DTO.
    /// </summary>
    /// <typeparam name="T">Entity type (business model)</typeparam>
    /// <typeparam name="D">DTO type (database table representation)</typeparam>
    /// <typeparam name="K">Key type</typeparam>
    public abstract class AbsSqlRepositoryK<T,D, K> : AbsSimpleSqlRepositoryK<T,D, K>,ISqlRepository<T,K> where T : class where D:class where K : notnull
    {
        /// <summary>
        /// Gets a value indicating whether optimized slim queries are used.
        /// </summary>
        /// <returns>True if slim queries are used; otherwise false.</returns>
        public bool UseSlim { get; } = false;
        /// <summary>
        /// Gets the table dependency used for change tracking.
        /// </summary>
        /// <returns>The table dependency.</returns>
        public SqlTableDependency TableDependency { get; private set; }
        /// <summary>
        /// Gets the change tracking priority.
        /// </summary>
        /// <returns>The change tracking priority.</returns>
        public ChangeTrackingPriority ChangesPriority { get; }
        /// <summary>
        /// Gets the current change tracking version of the table.
        /// </summary>
        /// <returns>The tracking version.</returns>
        public TrackingVersion Version => TableDependency.TableVersion;

        /// <summary>
        /// Creates the table dependency used for change tracking.
        /// </summary>
        /// <returns>The table dependency.</returns>
        protected virtual SqlTableDependency FactoryTableDependency()
        {
            return Database.GetTableDependency(typeof(D), ChangesPriority);
        }
        
        private void TableDependency_OnChanged(object? sender, DeltaChanges e)
        {
            if (ReloadOnAnyChange)
            {
                Reset();
            }
            else
                ResetIds(e.GetKeys<K>());
        }
        
        /// <summary>
        /// Constructor for SQL repository with Change Tracking
        /// </summary>
        /// <param name="database">Database instance</param>
        /// <param name="changesPriority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
        /// <param name="useslim">Use optimized slim queries (default: false)</param>
        protected AbsSqlRepositoryK(Database database, ChangeTrackingPriority? changesPriority = null, bool useslim = false) : base(database)
        {
            UseSlim = useslim;
            ChangesPriority = changesPriority ?? ChangeTrackingPriority.Default;
            // ReSharper disable once VirtualMemberCallInConstructor
            TableDependency = FactoryTableDependency();
            TableDependency.OnChanged += TableDependency_OnChanged;
        }
        
        /// <summary>
        /// Reloads all items from the database, mapping DTOs to entities.
        /// </summary>
        /// <returns>Result with the loaded items or the error.</returns>
        protected override EnumerableResult<T> IntReloadAll()
        {
            var ralldto = UseSlim ? Database.GetAllSlim<D>() : Database.GetAll<D>();
            if (ralldto.IsFailed) return ralldto.Error!;
            return new(ralldto.Value.Select(Map));
        }

        /// <summary>
        /// Reloads the items with the given keys from the database, mapping DTOs to entities.
        /// </summary>
        /// <param name="keys">Keys to reload.</param>
        /// <returns>Result with the loaded items or the error.</returns>
        protected override EnumerableResult<T> IntReloadSome(params IEnumerable<K> keys)
        {
            var alldto = UseSlim ? Database.GetSomeSlim<K, D>(keys) : Database.GetSome<K, D>(keys);
            return alldto.Map(Map);
        }

        // SD-17: al disponer el repo se desuscribe del change tracking para que
        // los deltas dejen de llegar (la dependencia y la BD son compartidas).
        /// <summary>
        /// Unsubscribes from the table dependency and disposes the repository.
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
}