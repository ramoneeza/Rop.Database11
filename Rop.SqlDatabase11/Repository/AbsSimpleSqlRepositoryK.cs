using System.Collections;
using System.Collections.Frozen;
using System.Reflection;

namespace Rop.Database11.Repository
{
    /// <summary>
    /// Interface for a simple SQL repository.
    /// </summary>
    public interface IAbsSimpleSqlRepository
    {
        /// <summary>
        /// Raised when the repository content changes.
        /// </summary>
        event EventHandler? OnChange;
        /// <summary>
        /// Gets the database instance used by the repository.
        /// </summary>
        /// <returns>The database instance.</returns>
        Database Database { get; }
        /// <summary>
        /// Gets the key description metadata for the repository.
        /// </summary>
        /// <returns>The key description.</returns>
        KeyDescription KeyDescription { get; }
        /// <summary>
        /// Gets a value indicating whether the last operation reported an error.
        /// </summary>
        /// <returns>True if there is a pending error; otherwise false.</returns>
        bool HasError { get; }
        /// <summary>
        /// Gets the error reported by the last operation, if any.
        /// </summary>
        /// <returns>The last error, or null.</returns>
        Error? LastError { get; }
        /// <summary>
        /// Gets a value indicating whether the repository must reload all items.
        /// </summary>
        /// <returns>True if a full reload is pending; otherwise false.</returns>
        bool MustReload { get; }
        /// <summary>
        /// Gets a value indicating whether the repository has been initialized.
        /// </summary>
        /// <returns>True if the repository is initialized; otherwise false.</returns>
        bool Initialized { get; }
        /// <summary>
        /// Initializes the repository, loading all items from the database.
        /// </summary>
        void Init();
        /// <summary>
        /// Checks whether a reload is pending and performs it.
        /// </summary>
        void Check();
        /// <summary>
        /// Forces a full reload of the repository.
        /// </summary>
        /// <param name="avoidsendchanges">When true, change events are not raised.</param>
        void Reset(bool avoidsendchanges = false);
    }
    /// <summary>
    /// Interface for a simple SQL repository with specific types.
    /// </summary>
    /// <typeparam name="T">Entity Type</typeparam>
    /// <typeparam name="K">Key Type</typeparam>
    public interface IAbsSimpleSqlRepository<T,K>:IAbsSimpleSqlRepository where T : class where K : notnull
    {
        /// <summary>
        /// Raised when a set of records changed, with the affected keys.
        /// </summary>
        event EventHandler<RecordsChangedEventArgs<K>>? OnRecordsChanged;
        /// <summary>
        /// Raised when changed items are available after a reload.
        /// </summary>
        event EventHandler<RecordsChangedEventArgs<T>>? OnItemsChanged;
        /// <summary>
        /// Raised when a full reload fails (instead of OnItemsChanged).
        /// </summary>
        event EventHandler<Error>? OnError;

        /// <summary>
        /// Gets the key of the given entity item.
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>Key of the item.</returns>
        K GetTKey(T item);
        /// <summary>
        /// Reloads the items with the given keys from the database.
        /// </summary>
        /// <param name="changes">Keys to reload.</param>
        /// <returns>True if the reload succeeded; otherwise false.</returns>
        bool ResetIds(params IReadOnlyCollection<K> changes);
        /// <summary>
        /// Gets the item with the given key from the in-memory repository.
        /// </summary>
        /// <param name="key">Key of the item.</param>
        /// <param name="error">Error reported by the operation, if any.</param>
        /// <returns>The item, or null if not found.</returns>
        T? Get(K key, out Error? error);
        /// <summary>
        /// Gets all items from the in-memory repository.
        /// </summary>
        /// <param name="error">Error reported by the operation, if any.</param>
        /// <returns>List with all items.</returns>
        List<T> GetAll(out Error? error);
        /// <summary>
        /// Gets the items with the given keys from the in-memory repository.
        /// </summary>
        /// <param name="keys">Keys of the items.</param>
        /// <param name="error">Error reported by the operation, if any.</param>
        /// <returns>List with the requested items.</returns>
        List<T> GetSome(IEnumerable<K> keys, out Error? error);
        /// <summary>
        /// Gets all items as a frozen dictionary keyed by the entity key.
        /// </summary>
        /// <param name="error">Error reported by the operation, if any.</param>
        /// <returns>Frozen dictionary with all items.</returns>
        FrozenDictionary<K, T> GetAllDictionary(out Error? error);
    }
    /// <summary>
    /// Base abstract class for a simple SQL repository with specific entity type, dto type and key type.
    /// </summary>
    /// <typeparam name="T">Entity Type</typeparam>
    /// <typeparam name="D">Dto type as in database table with dapper decorators</typeparam>
    /// <typeparam name="K">Key type</typeparam>
    public abstract class AbsSimpleSqlRepositoryK<T,D, K>:IAbsSimpleSqlRepository<T,K>,IDisposable where T : class where D:class where K : notnull
    {
        /// <summary>
        /// Raised when the repository content changes.
        /// </summary>
        public event EventHandler? OnChange;
        /// <summary>
        /// Raised when a set of records changed, with the affected keys.
        /// </summary>
        public event EventHandler<RecordsChangedEventArgs<K>>? OnRecordsChanged;
        /// <summary>
        /// Raised when changed items are available after a reload.
        /// </summary>
        public event EventHandler<RecordsChangedEventArgs<T>>? OnItemsChanged;
        /// <summary>
        /// Raised when a full reload fails (instead of OnItemsChanged).
        /// </summary>
        public event EventHandler<Error>? OnError;
        /// <summary>
        /// Gets the database instance used by the repository.
        /// </summary>
        /// <returns>The database instance.</returns>
        public Database Database { get; }
        /// <summary>
        /// Gets the key description metadata for the repository.
        /// </summary>
        /// <returns>The key description.</returns>
        public virtual KeyDescription KeyDescription { get; }
        /// <summary>
        /// Gets the cached accessor for the DTO key property.
        /// </summary>
        /// <returns>The cached property accessor.</returns>
        protected PropertyCache KeyPropD => KeyDescription.KeyPropCache;
        /// <summary>
        /// Gets the cached accessor for the entity key property.
        /// </summary>
        /// <returns>The cached property accessor.</returns>
        protected PropertyCache KeyPropT { get; }
        /// <summary>
        /// Maps a DTO item to the entity type.
        /// </summary>
        /// <param name="item">DTO instance.</param>
        /// <returns>Mapped entity.</returns>
        protected abstract T Map(D item);
        /// <summary>
        /// Gets the key of the given entity item.
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>Key of the item.</returns>
        public virtual K GetTKey(T item)=> (K)(KeyPropT.Getter(item)??throw new Exception("Can't get Key"));
        /// <summary>
        /// Gets the key of the given DTO item.
        /// </summary>
        /// <param name="item">DTO instance.</param>
        /// <returns>Key of the item.</returns>
        public virtual K GetDKey(D item)=> (K)(KeyDescription.GetKeyValue(item)??throw new Exception("Can't get Key"));
        /// <summary>
        /// Reloads all items from the database.
        /// </summary>
        /// <returns>Result with the loaded items or the error.</returns>
        protected abstract EnumerableResult<T> IntReloadAll();
        /// <summary>
        /// Reloads the items with the given keys from the database.
        /// </summary>
        /// <param name="keys">Keys to reload.</param>
        /// <returns>Result with the loaded items or the error.</returns>
        protected abstract EnumerableResult<T> IntReloadSome(params IEnumerable<K> keys);
        /// <summary>
        /// Gets the internal in-memory dictionary that holds the loaded items.
        /// </summary>
        /// <returns>The repository dictionary.</returns>
        protected RepositoryDictionary<K, T> Repository { get; }
        private readonly Lock _lockreload = new();
        /// <summary>
        /// Gets a value indicating whether the last operation reported an error.
        /// </summary>
        /// <returns>True if there is a pending error; otherwise false.</returns>
        public bool HasError => LastError != null;
        /// <summary>
        /// Gets or sets the error reported by the last operation, if any.
        /// </summary>
        /// <returns>The last error, or null.</returns>
        public Error? LastError { get; protected set; }
        /// <summary>
        /// Raises the change events after a reload, retrieving the affected items from the database.
        /// </summary>
        /// <param name="changes">Keys of the changed records, or null for a full reload.</param>
        protected void OnMustInvokeChanges(IReadOnlyCollection<K>? changes)
        {
            RepositoryChanged(changes);
            OnChange?.Invoke(this, EventArgs.Empty);
            OnRecordsChanged?.Invoke(this, new RecordsChangedEventArgs<K>(changes));
            if (changes == null)
            {
                var all = GetAll(out var error);
                if (error != null) OnError?.Invoke(this, error);
                else OnItemsChanged?.Invoke(this, new RecordsChangedEventArgs<T>(all));
            }
            else
            {
                var some = GetSome(changes, out var error);
                if (error != null) OnError?.Invoke(this, error);
                else OnItemsChanged?.Invoke(this, new RecordsChangedEventArgs<T>(some));
            }
        }
        /// <summary>
        /// Gets a value indicating whether the repository must reload all items.
        /// </summary>
        /// <returns>True if a full reload is pending; otherwise false.</returns>
        public bool MustReload { get; private set; } = true;
        /// <summary>
        /// Gets a value indicating whether the repository has been initialized.
        /// </summary>
        /// <returns>True if the repository is initialized; otherwise false.</returns>
        public bool Initialized { get; private set; } = false;
        /// <summary>
        /// Gets a value indicating whether any change triggers a full reload instead of a partial reload of the changed keys.
        /// </summary>
        /// <returns>True if any change triggers a full reload; otherwise false.</returns>
        protected virtual bool ReloadOnAnyChange => false;
        /// <summary>
        /// Forces a full reload of the repository.
        /// </summary>
        /// <param name="avoidsendchanges">When true, change events are not raised.</param>
        public void Reset(bool avoidsendchanges = false)
        {
            lock (_lockreload)
            {
                MustReload = true;
                ReloadAll(avoidsendchanges);
            }
        }
        /// <summary>
        /// Initializes the repository, loading all items from the database if not already initialized.
        /// </summary>
        public virtual void Init()
        {
            lock (_lockreload)
            {

                if (!Initialized)
                    ReloadAll(true);
            }
        }
        /// <summary>
        /// Checks whether a reload is pending and performs it.
        /// </summary>
        public virtual void Check()
        {
            lock (_lockreload)
            {
                if (MustReload) ReloadAll();
            }
        }
        /// <summary>
        /// Reloads all items from the database and updates the repository state.
        /// </summary>
        /// <param name="avoidsendchanges">When true, change events are not raised.</param>
        /// <returns>Result indicating success or the error.</returns>
        protected VoidResult ReloadAll(bool avoidsendchanges = false)
        {
            LastError = null;
            try
            {
                var rall = IntReloadAll();
                if (rall.IsFailed)
                {
                    LastError = rall.Error;
                    return rall;
                }

                Repository.ReplaceAll(rall.Value, InRange);
                MustReload = false;
                Initialized = true;
                if (!avoidsendchanges)
                    OnMustInvokeChanges(null);
                else
                    RepositoryChanged(null);
                return VoidResult.Ok;
            }
            catch (Exception ex)
            {
               LastError = new ExceptionError(ex);
               return LastError;
            }
        }
        /// <summary>
        /// Reloads the items with the given keys (non-generic convenience overload).
        /// </summary>
        /// <param name="changes">Keys to reload.</param>
        /// <returns>True if the reload succeeded; otherwise false.</returns>
        public bool ResetIds(IEnumerable changes) => ResetIds(changes.Cast<K>().ToArray());
        /// <summary>
        /// Reloads the items with the given keys from the database and raises the change events.
        /// </summary>
        /// <param name="changes">Keys to reload.</param>
        /// <returns>True if the reload succeeded; otherwise false.</returns>
        public bool ResetIds(params IReadOnlyCollection<K> changes)
        {
            if (changes.Count == 0) return true;
            // SD-9: mismo lock que Reset/Init/Check para no solaparse con un ReloadAll
            // concurrente (el lock es reentrante: los eventos del llamador pueden
            // volver a entrar sin bloquearse).
            lock (_lockreload)
            {
                try
                {
                    var rreload = IntReloadSome(changes);
                    if (rreload.IsFailed)
                    {
                        LastError = rreload.Error;
                        MustReload = true;
                        return false;
                    }
                    Repository.ReplaceSome(changes,rreload.Value,InRange);
                    OnMustInvokeChanges(changes);
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = new ExceptionError(ex);
                    MustReload = true;
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets the item with the given key from the in-memory repository, reloading first if pending.
        /// </summary>
        /// <param name="key">Key of the item.</param>
        /// <param name="error">Error reported by the operation, if any.</param>
        /// <returns>The item, or null if not found.</returns>
        public virtual T? Get(K key, out Error? error)
        {
            error = null;
            Check();
            error = LastError;
            if (key is int and <= 0) return null;
            if (key is string s && string.IsNullOrEmpty(s)) return null;
            return Repository.Get(key);
        }
        /// <summary>
        /// Creates the in-memory dictionary used to store the loaded items.
        /// </summary>
        /// <returns>The repository dictionary.</returns>
        protected virtual RepositoryDictionary<K, T> FactoryDictionary()=> new RepositoryDictionary<K, T>(GetTKey);
        // Constructor
        /// <summary>
        /// Initializes the repository using the key description derived from the DTO type.
        /// </summary>
        /// <param name="database">Database instance.</param>
        protected AbsSimpleSqlRepositoryK(Database database):this(database, DapperHelperExtend.GetKeyDescription<D>())
        {
        }
        /// <summary>
        /// Initializes the repository with an explicit key description.
        /// </summary>
        /// <param name="database">Database instance.</param>
        /// <param name="keydescription">Key description metadata for the DTO type.</param>
        protected AbsSimpleSqlRepositoryK(Database database,KeyDescription keydescription)
        {
            // La resolucion de BD externa usa el KeyDescription recibido (no el del tipo D):
            // en los repos parciales D es PartialBlock<K,D>, que no tiene atributos de tabla/clave.
            Database = database.FactoryExternalDatabase(keydescription);
            KeyDescription = keydescription;
            var keyPropT=FindKeyPropT(keydescription) ?? throw new ArgumentException($"Type {typeof(T)} has not key");
            KeyPropT = new PropertyCache(keyPropT);
            // ReSharper disable once VirtualMemberCallInConstructor
            //Repository = new RepositoryDictionary<K, T>(GetTKey);
            Repository = FactoryDictionary();
        }
        /// <summary>
        /// Resolves the property of the entity type that holds the repository key.
        /// Overridable so that wrapper entity types (like the partial blocks) can
        /// expose the key through a different property name.
        /// </summary>
        /// <param name="keydescription">Key description of the DTO type.</param>
        /// <returns>The key property of the entity type, or null when it does not exist.</returns>
        protected virtual PropertyInfo? FindKeyPropT(KeyDescription keydescription)
            => typeof(T).GetProperty(keydescription.KeyProp.Name);
        
        /// <summary>
        /// Gets all items from the in-memory repository, reloading first if pending.
        /// </summary>
        /// <param name="error">Error reported by the operation, if any.</param>
        /// <returns>List with all items.</returns>
        public virtual List<T> GetAll(out Error? error)
        {
            error = null;
            Check();
            error = LastError;
            return Repository.Values;
        }
        /// <summary>
        /// Gets the items with the given keys from the in-memory repository, reloading first if pending.
        /// </summary>
        /// <param name="keys">Keys of the items.</param>
        /// <param name="error">Error reported by the operation, if any.</param>
        /// <returns>List with the requested items.</returns>
        public virtual List<T> GetSome(IEnumerable<K> keys, out Error? error)
        {
            error = null;
            Check();
            error = LastError;
            return Repository.GetSome(keys.ToArray());
        }
        /// <summary>
        /// Gets all items as a frozen dictionary keyed by the entity key.
        /// </summary>
        /// <param name="error">Error reported by the operation, if any.</param>
        /// <returns>Frozen dictionary with all items.</returns>
        public FrozenDictionary<K, T> GetAllDictionary(out Error? error)
        {
            var r = GetAll(out error);
            return r.ToFrozenDictionary(GetTKey);
        }
        /// <summary>
        /// Gets the items that match the given filter from the in-memory repository.
        /// </summary>
        /// <param name="filter">Predicate that selects the items.</param>
        /// <param name="error">Error reported by the operation, if any.</param>
        /// <returns>List with the matching items.</returns>
        public List<T> GetFiltered(Func<T, bool> filter, out Error? error)
        {
            error = null;
            Check();
            error = LastError;
            return Repository.Where(filter);
        }
        /// <summary>
        /// Notification hook invoked when the repository content changes.
        /// </summary>
        /// <param name="changes">Keys of the changed records, or null for a full reload.</param>
        protected virtual void RepositoryChanged(IReadOnlyCollection<K>? changes)
        {
        }
        /// <summary>
        /// Determines whether an item is included in the repository.
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>True if the item is in range; otherwise false.</returns>
        protected virtual bool InRange(T item) => true;

        /// <summary>
        /// Disposes the repository. Derived classes release their own resources
        /// (change tracking subscription, memory cache) in <see cref="Dispose(bool)"/>.
        /// The shared <see cref="Database"/> is NOT disposed (instances are cached).
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Disposable pattern hook for derived repositories.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
        }
    }

    /// <summary>
    /// Base abstract class for a simple SQL repository with specific entity type and dto type, using int as key type.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <typeparam name="D">DTO type (database table representation)</typeparam>
    /// <param name="database">Database instance</param>
    public abstract class AbsSimpleSqlIntRepository<T,D>(Database database)
        : AbsSimpleSqlRepositoryK<T,D, int>(database) where T : class where D: class
    {
    }
    /// <summary>
    /// Base abstract class for a simple SQL repository with specific entity type and dto type, using string as key type.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <typeparam name="D">DTO type (database table representation)</typeparam>
    /// <param name="database">Database instance</param>
    public abstract class AbsSimpleSqlRepository<T,D>(Database database)
        : AbsSimpleSqlRepositoryK<T,D, string>(database) where T : class where D : class
    {
    }
    /// <summary>
    /// Base abstract class for a simple SQL repository with specific entity type, using string as key type without specific DTO.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="database">Database instance</param>
    public abstract class AbsSimpleSqlRepository<T>(Database database)
        : AbsSimpleSqlRepository<T, T>(database) where T : class
    {
        /// <summary>
        /// Maps an entity to itself (identity mapping).
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>The same entity.</returns>
        protected override T Map(T item)=> item;
    }
    /// <summary>
    /// Base abstract class for a simple SQL repository with specific entity type, using int as key type without specific DTO.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="database">Database instance</param>
    public abstract class AbsSimpleSqlIntRepository<T>(Database database)
        : AbsSimpleSqlIntRepository<T, T>(database) where T : class
    {
        /// <summary>
        /// Maps an entity to itself (identity mapping).
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>The same entity.</returns>
        protected override T Map(T item)=> item;
    }
    
}