using System.Collections;
using System.Collections.Frozen;
using System.Reflection;
using Rop.Database11.Repository;

namespace Rop.Database11.CacheRepository
{
    /// <summary>
    /// Base class for a simple SQL repository that expires after a specified time with key of type K and DTO.
    /// </summary>
    /// <typeparam name="T">Entity type stored in the cache.</typeparam>
    /// <typeparam name="D">DTO type used to load data from the database.</typeparam>
    /// <typeparam name="K">Type of the repository key.</typeparam>
    public abstract class AbsSimpleCacheSqlRepositoryK<T, D, K> : IAbsSimpleSqlRepository<T, K>, IDisposable
        where T : class where D : class where K : notnull
    {
        /// <summary>
        /// Raised when the cached data changes.
        /// </summary>
        public event EventHandler? OnChange;
        /// <summary>
        /// Raised with the keys affected by a change.
        /// </summary>
        public event EventHandler<RecordsChangedEventArgs<K>>? OnRecordsChanged;
        /// <summary>
        /// Raised with the entities affected by a change.
        /// </summary>
        public event EventHandler<RecordsChangedEventArgs<T>>? OnItemsChanged;
        /// <summary>
        /// Raised when an error occurs while notifying item changes.
        /// </summary>
        public event EventHandler<Error>? OnError;
        /// <summary>
        /// Default time after which a cached entry expires.
        /// </summary>
        public TimeSpan DefaultExpirationTime { get; }
        /// <summary>
        /// Default interval at which the cache checks for expired entries.
        /// </summary>
        public TimeSpan DefaultExpirationCleanTime { get; }
        /// <summary>
        /// Database used to load the data.
        /// </summary>
        public Database Database { get; }
        /// <summary>
        /// PropertyInfo of the key in the DTO type.
        /// </summary>
        protected PropertyInfo KeyPropD => KeyDescription.KeyProp;
        /// <summary>
        /// Description of the main key of the DTO type.
        /// </summary>
        public KeyDescription KeyDescription { get; }
        /// <summary>
        /// PropertyInfo of the key in the entity type.
        /// </summary>
        protected PropertyInfo KeyPropT { get; }
        /// <summary>
        /// In-memory cache of entities.
        /// </summary>
        protected MemoryCache<K, T> Repository { get; }
        /// <summary>
        /// Escaped SQL statement used to load the full key list.
        /// </summary>
        protected string SqlLoadKeys { get; }
        private readonly Lock _lockdic = new();

        // Constructor


        /// <summary>
        /// Initializes the repository and its in-memory cache.
        /// </summary>
        /// <param name="database">Database instance.</param>
        /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
        /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
        protected AbsSimpleCacheSqlRepositoryK(Database database, TimeSpan? defaultExpirationTime = null,
            TimeSpan? defaultcleantime = null)
        {
            Database = database.FactoryExternalDatabase<D>();
            KeyDescription = DapperHelperExtend.GetKeyDescription<D>() ??
                             throw new ArgumentException($"Type {typeof(D)} has not key");
            KeyPropT = typeof(T).GetProperty(KeyDescription.KeyProp.Name) ??
                       throw new ArgumentException($"Type {typeof(T)} has not key");
            DefaultExpirationTime = defaultExpirationTime ?? TimeSpan.FromMinutes(5);
            DefaultExpirationCleanTime = defaultcleantime ?? TimeSpan.FromMinutes(10);
            var eq = (Type.GetTypeCode(typeof(K)) == TypeCode.String)
                ? (IEqualityComparer<K>)StringComparer.OrdinalIgnoreCase
                : EqualityComparer<K>.Default;
            Repository = new MemoryCache<K, T>(GetTKey, DefaultExpirationTime, DefaultExpirationCleanTime, eq);
            SqlLoadKeys = $"SELECT {DapperHelperExtend.SqlName(KeyDescription.KeyName)} FROM {DapperHelperExtend.SqlName(KeyDescription.TableName)}";
        }

        private Result<T> IntGet(K key)
        {
            var r = IntReloadSome([key]);
            if (r.IsFailed) return r.Error!;
            if (r.Value.Count == 0) return Error.Null();
            return r.Value[0];
        }

        /// <summary>
        /// Maps a DTO instance to its entity representation.
        /// </summary>
        /// <param name="item">DTO instance loaded from the database.</param>
        /// <returns>Entity instance.</returns>
        protected abstract T Map(D item);
        /// <summary>
        /// Gets the key of an entity.
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>Key value.</returns>
        public virtual K GetTKey(T item) => (K)(KeyPropT.GetValue(item) ?? throw new Exception("Can't get Key"));
        /// <summary>
        /// Gets the key of a DTO.
        /// </summary>
        /// <param name="item">DTO instance.</param>
        /// <returns>Key value.</returns>
        public virtual K GetDKey(D item) => (K)(KeyDescription.KeyProp.GetValue(item) ?? throw new Exception("Can't get Key"));


        /// <summary>
        /// Loads the full key list from the database and synchronizes the cache keys.
        /// Marks the repository as initialized when successful.
        /// </summary>
        /// <returns>Result with the loaded keys, or the error that occurred.</returns>
        protected EnumerableResult<K> IntReloadAllKeys()
        {
            // SD-4: la consulta a BD va fuera del lock.
            var keys = Database.Query<K>(SqlLoadKeys);
            if (keys.IsFailed)
            {
                LastError = keys.Error!;
                return LastError;
            }

            lock (_lockdic)
            {
                LastError = null;
                Repository.ForceKeys(keys.Value!);
                MustReload = false;
                Initialized = true;
            }
            return keys;
        }

        /// <summary>
        /// Loads the entities for the given keys from the database.
        /// </summary>
        /// <param name="keys">Keys to load.</param>
        /// <returns>Result with the loaded entities, or the error that occurred.</returns>
        protected abstract EnumerableResult<T> IntReloadSome(params IEnumerable<K> keys);
        /// <summary>
        /// Loads all entities from the database.
        /// </summary>
        /// <returns>Result with the loaded entities, or the error that occurred.</returns>
        protected abstract EnumerableResult<T> IntReloadAll();
        /// <summary>
        /// Indicates whether the repository currently has an error.
        /// </summary>
        public bool HasError => LastError != null;
        /// <summary>
        /// Last error produced by a database operation, if any.
        /// </summary>
        public Error? LastError { get; protected set; }

        /// <summary>
        /// Raises the change events after a cache update.
        /// </summary>
        /// <param name="changes">Keys affected by the change, or null for a full change.</param>
        protected void OnMustInvokeChanges(IReadOnlyCollection<K>? changes)
        {
            RepositoryChanged(changes);
            OnChange?.Invoke(this, EventArgs.Empty);
            OnRecordsChanged?.Invoke(this, new RecordsChangedEventArgs<K>(changes));
            if (OnItemsChanged != null)
            {
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
        }

        /// <summary>
        /// Indicates whether the cached data must be reloaded.
        /// </summary>
        public bool MustReload { get; private set; } = true;
        /// <summary>
        /// Indicates whether the repository has been initialized with the key list.
        /// </summary>
        public bool Initialized { get; private set; } = false;
        /// <summary>
        /// When true, any change forces a full reload. Default is false.
        /// </summary>
        protected virtual bool ReloadOnAnyChange => false;

        /// <summary>
        /// Marks the repository as needing reload and reloads the key list.
        /// </summary>
        /// <param name="avoidsendchanges">Indicates whether change notifications should be suppressed (not used in the current implementation).</param>
        public void Reset(bool avoidsendchanges = false)
        {
            MustReload = true;
            IntReloadAllKeys();
        }

        /// <summary>
        /// Initializes the repository by loading the key list if it was not already initialized.
        /// </summary>
        public virtual void Init()
        {
            if (!Initialized) IntReloadAllKeys();
        }

        /// <summary>
        /// Reloads the key list if the repository is marked as needing reload.
        /// </summary>
        public virtual void Check()
        {
            if (MustReload) IntReloadAllKeys();
        }

        /// <summary>
        /// Runs a delegate under the repository lock.
        /// </summary>
        /// <typeparam name="R">Type of the result of the delegate.</typeparam>
        /// <param name="a">Delegate to run.</param>
        /// <returns>Result of the delegate.</returns>
        protected R LockUnitOfWork<R>(Func<R> a)
        {
            lock (_lockdic)
            {
                return a();
            }
        }

        /// <summary>
        /// Forces the expiration of the cache entries for the given keys.
        /// </summary>
        /// <param name="changes">Keys to expire.</param>
        /// <returns>True if the operation succeeded.</returns>
        public bool ResetIds(IEnumerable changes) => ResetIds(changes.Cast<K>().ToArray());

        /// <summary>
        /// Forces the expiration of the cache entries for the given keys and raises the change events.
        /// </summary>
        /// <param name="changes">Keys to expire.</param>
        /// <returns>True if the operation succeeded; false if an exception occurred.</returns>
        public bool ResetIds(params IReadOnlyCollection<K> changes)
        {
            if (changes.Count == 0) return true;
            try
            {
                var r = LockUnitOfWork(() =>
                {
                    Repository.Expires(changes);
                    return true;
                });
                return r;
            }
            catch (Exception ex)
            {
                LastError = new ExceptionError(ex);
                return false;
            }
            finally
            {
                OnMustInvokeChanges(changes);
            }
        }

        /// <summary>
        /// Gets an entity by key, using the cache and reloading it from the database when expired.
        /// </summary>
        /// <param name="key">Key of the entity.</param>
        /// <param name="error">Error, if any. When a real error occurs and a stale cached value exists, that value is returned and the error is reported.</param>
        /// <returns>Entity with the given key, or null when it does not exist (or cannot be loaded).</returns>
        public virtual T? Get(K key, out Error? error)
        {
            error = null;
            Check();
            // SD-4: lectura del cache con su lock interno; la recarga (BD) va fuera de locks.
            var r = Repository.GetValueOrDefault(key);
            if (r is ValidItem<T> vi) return vi.Value;
            var r2 = IntGet(key);
            if (r2.IsFailedAndNotNull)
            {
                // error real: mal menor devolviendo el valor expirado que quede en cache
                // (paridad con r?.Value: tras expirar el valor ya no esta disponible)
                error = r2.Error;
                LastError = r2.Error;
                return r is ValidItem<T> vi2 ? vi2.Value : default;
            }
            if (r2.IsFailed) return null; // no encontrado (NullError)
            Repository.Add(r2.Value!);
            return r2.Value;
        }

        /// <summary>
        /// Gets all entities, reloading the expired ones from the database.
        /// </summary>
        /// <param name="error">Error, if any. When a real error occurs, the values still in cache (even expired) are returned.</param>
        /// <returns>All cached entities.</returns>
        public virtual List<T> GetAll(out Error? error)
        {
            error = null;
            Check();
            // SD-4: RefreshAll gestiona sus propios locks por fases; la recarga va fuera.
            var r = Repository.RefreshAll((count, keys) =>
            {
                if (keys.Count > count * 0.75)
                {
                    return IntReloadAll();
                }
                else
                {
                    return IntReloadSome(keys);
                }
            });
            if (r.IsFailed)
            {
                // mal menor: devolver los datos que quedan en memoria (aunque expirados)
                error = r.Error;
                LastError = r.Error;
                return Repository.GetAllValues();
            }
            return r.Value!.ToList();
        }

        /// <summary>
        /// Gets the entities for the given keys, reloading the expired ones from the database.
        /// </summary>
        /// <param name="keys">Keys to retrieve.</param>
        /// <param name="error">First real error found, if any.</param>
        /// <returns>Entities found (missing keys are skipped).</returns>
        public List<T> GetSome(IEnumerable<K> keys, out Error? error)
        {
            error = null;
            Check();
            var res = new List<T>();
            foreach (var key in keys)
            {
                var r = Repository.GetValueOrDefault(key);
                if (r is ValidItem<T> vi)
                {
                    res.Add(vi.Value);
                    continue;
                }

                var r2 = IntGet(key);
                if (r2.IsFailedAndNotNull)
                {
                    // error real: mal menor con el valor expirado si existe
                    // (paridad con r?.Value: tras expirar el valor ya no esta disponible)
                    error ??= r2.Error;
                    LastError = r2.Error;
                    if (r is ValidItem<T> vi2) res.Add(vi2.Value);
                    continue;
                }
                if (r2.IsFailed) continue; // no encontrado (NullError) → saltar
                Repository.Add(r2.Value!);
                res.Add(r2.Value!);
            }

            return res;
        }

        /// <summary>
        /// Gets all entities as a frozen dictionary keyed by the entity key.
        /// </summary>
        /// <param name="error">Error, if any.</param>
        /// <returns>Frozen dictionary with all cached entities.</returns>
        public FrozenDictionary<K, T> GetAllDictionary(out Error? error)
        {
            var r = GetAll(out error);
            return r.ToFrozenDictionary(GetTKey);
        }

        /// <summary>
        /// Hook invoked when the cached data changes, before raising the events.
        /// </summary>
        /// <param name="changes">Keys affected by the change, or null for a full change.</param>
        protected virtual void RepositoryChanged(IReadOnlyCollection<K>? changes)
        {
        }

        /// <summary>
        /// Indicates whether an entity is in the valid range for this repository.
        /// </summary>
        /// <param name="item">Entity to check.</param>
        /// <returns>True if the entity is valid.</returns>
        protected virtual bool InRange(T item) => true;

        /// <summary>
        /// Disposes the repository and its memory cache (stops the cleanup timer).
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
            if (disposing)
            {
                Repository.Dispose();
            }
        }



    }


    /// <summary>
    /// Base class for a simple SQL repository that expires after a specified time with key of type int and with DTO.
    /// </summary>
    /// <typeparam name="T">Entity type (business model).</typeparam>
    /// <typeparam name="D">DTO type (database table representation).</typeparam>
    /// <param name="database">Database instance.</param>
    /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
    /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
    public abstract class AbsSimpleCacheSqlIntRepository<T, D>(
        Database database,
        TimeSpan? defaultExpirationTime = null,
        TimeSpan? defaultcleantime = null)
        : AbsSimpleCacheSqlRepositoryK<T, D, int>(database, defaultExpirationTime, defaultcleantime)
        where T : class
        where D : class
    {
    }

    /// <summary>
    /// Base class for a simple SQL repository that expires after a specified time with key of type string and with DTO.
    /// </summary>
    /// <typeparam name="T">Entity type (business model).</typeparam>
    /// <typeparam name="D">DTO type (database table representation).</typeparam>
    /// <param name="database">Database instance.</param>
    /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
    /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
    public abstract class AbsSimpleCacheSqlRepository<T, D>(
        Database database,
        TimeSpan? defaultExpirationTime = null,
        TimeSpan? defaultcleantime = null)
        : AbsSimpleCacheSqlRepositoryK<T, D, string>(database, defaultExpirationTime, defaultcleantime)
        where T : class
        where D : class
    {
    }

    /// <summary>
    /// Base class for a simple SQL repository that expires after a specified time with key of type int and without DTO.
    /// </summary>
    /// <typeparam name="T">Entity type, used both as entity and DTO.</typeparam>
    /// <param name="database">Database instance.</param>
    /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
    /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
    public abstract class AbsSimpleCacheSqlIntRepository<T>(
        Database database,
        TimeSpan? defaultExpirationTime = null,
        TimeSpan? defaultcleantime = null)
        : AbsSimpleCacheSqlIntRepository<T, T>(database, defaultExpirationTime, defaultcleantime) where T : class
    {
        /// <summary>
        /// Maps a DTO instance to its entity representation (identity mapping in this case).
        /// </summary>
        /// <param name="item">DTO instance loaded from the database.</param>
        /// <returns>Entity instance.</returns>
        protected override T Map(T item) => item;
    }

    /// <summary>
    /// Base class for a simple SQL repository that expires after a specified time with key of type string and without DTO.
    /// </summary>
    /// <typeparam name="T">Entity type, used both as entity and DTO.</typeparam>
    /// <param name="database">Database instance.</param>
    /// <param name="defaultExpirationTime">Time before cache entries expire (default: 5 minutes).</param>
    /// <param name="defaultcleantime">Interval at which the cache checks for expired entries (default: 10 minutes).</param>
    public abstract class AbsSimpleCacheSqlRepository<T>(
        Database database,
        TimeSpan? defaultExpirationTime = null,
        TimeSpan? defaultcleantime = null)
        : AbsSimpleCacheSqlRepository<T, T>(database, defaultExpirationTime, defaultcleantime) where T : class
    {
        /// <summary>
        /// Maps a DTO instance to its entity representation (identity mapping in this case).
        /// </summary>
        /// <param name="item">DTO instance loaded from the database.</param>
        /// <returns>Entity instance.</returns>
        protected override T Map(T item) => item;
    }
}
