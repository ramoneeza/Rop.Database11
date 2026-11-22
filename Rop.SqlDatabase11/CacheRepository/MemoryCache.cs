using System.Diagnostics;

namespace Rop.Database11.CacheRepository;

/// <summary>
/// Thread-safe in-memory cache with automatic expiration and a background cleanup timer.
/// </summary>
/// <typeparam name="K">Type of the cache key.</typeparam>
/// <typeparam name="T">Type of the cached values.</typeparam>
public class MemoryCache<K, T> : IDisposable where K : notnull where T : class
{
    private readonly Dictionary<K, CacheState<T>> _cache;
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _defaultExpiration;
    private readonly Lock _lock = new();
    private readonly IEqualityComparer<K> _comparer;
    private bool _disposed;
    /// <summary>
    /// Raised when cache entries are cleaned up (expired, removed or cleared).
    /// </summary>
    public event EventHandler<CacheCleanUpArgs<K>>? CacheCleanUp;
    private Func<T, K> _getKeyFn;
    /// <summary>
    /// Initializes the cache and starts the cleanup timer.
    /// </summary>
    /// <param name="getKeyFn">Function that extracts the key from a value.</param>
    /// <param name="defaultExpiration">Default time before an entry expires.</param>
    /// <param name="cleanupInterval">Interval at which the cleanup timer runs.</param>
    /// <param name="comparer">Key comparer (optional).</param>
    public MemoryCache(Func<T,K> getKeyFn, TimeSpan defaultExpiration, TimeSpan cleanupInterval, IEqualityComparer<K>? comparer)
    {
        _getKeyFn= getKeyFn;
        _comparer = comparer ?? EqualityComparer<K>.Default;
        _cache = new Dictionary<K, CacheState<T>>(comparer);
        _defaultExpiration = defaultExpiration;
        // A timer cleans up expired entries periodically
        _cleanupTimer = new Timer(_cleanup, null, cleanupInterval, cleanupInterval);
    }
    /// <summary>
    /// Adds or replaces a value in the cache.
    /// </summary>
    /// <param name="value">Value to cache.</param>
    /// <param name="expiration">Expiration time for this entry, or null to use the default.</param>
    public void Add(T value, TimeSpan? expiration = null)
    {
        var key= _getKeyFn(value);
        lock (_lock)
        {
            var expireAt = DateTime.UtcNow + (expiration ?? _defaultExpiration);
            _cache[key] = new ValidItem<T>(value, expireAt);
        }
    }
    /// <summary>
    /// Adds or replaces a set of values in the cache with a common expiration time.
    /// </summary>
    /// <param name="items">Values to cache.</param>
    /// <param name="expiration">Expiration time for the entries, or null to use the default.</param>
    public void AddRange(IEnumerable<T> items, TimeSpan? expiration)
    {
        lock (_lock)
        {
            var expireAt = DateTime.UtcNow + (expiration ?? _defaultExpiration);
            foreach (var value in items)
            {
                var key=_getKeyFn(value);
                _cache[key] = new ValidItem<T>(value, expireAt);
            }
        }
    }
    /// <summary>
    /// Adds or replaces a set of values in the cache using the default expiration time.
    /// </summary>
    /// <param name="items">Values to cache.</param>
    public void AddRange(params IEnumerable<T> items) => AddRange(items, null);
    /// <summary>
    /// Adds or replaces a set of values in the cache with a common expiration time.
    /// </summary>
    /// <param name="expiration">Expiration time for the entries, or null to use the default.</param>
    /// <param name="items">Values to cache.</param>
    public void AddRange(TimeSpan? expiration, params IEnumerable<T> items) => AddRange(items, expiration);
    /// <summary>
    /// Tries to get a cached value, expiring it in place if its time has passed.
    /// </summary>
    /// <param name="key">Key of the value.</param>
    /// <returns>State of the cached entry, or null when the key is not present.
    /// When an entry expires on this access, its value is disposed and a
    /// <see cref="JustExpiredItem"/> is returned (a <see cref="CacheCleanUpArgs{K}"/>
    /// event is raised for that key).</returns>
    public CacheState<T>? TryGetValue(K key)
    {
        CacheState<T>? result;
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var state)) result = null;
            else
            {
                result = state switch
                {
                    // U-2: the state is computed by a single switch that compares ExpireAt.
                    ValidItem<T> vi when vi.ExpireAt < DateTime.UtcNow => _expireNow(key, vi),
                    _ => state,
                };
            }
        }
        if (result is JustExpiredItem) OnCacheCleanUp(key);
        return result;
    }

    private CacheState<T> _expireNow(K key, ValidItem<T> vi)
    {
        if (vi.Value is IDisposable d) d.Dispose();
        _cache[key] = new ExpiredItem();
        return new JustExpiredItem();
    }

    /// <summary>
    /// Gets the cached item for a key, or null when not present.
    /// </summary>
    /// <param name="key">Key of the value.</param>
    /// <returns>State of the cached entry, or null.</returns>
    public CacheState<T>? GetValueOrDefault(K key) => TryGetValue(key);
    /// <summary>
    /// Raises the <see cref="CacheCleanUp"/> event, isolating handler exceptions.
    /// </summary>
    /// <param name="keys">Keys cleaned up.</param>
    protected virtual void OnCacheCleanUp(params IReadOnlyCollection<K> keys)
    {
        var handler = CacheCleanUp;
        if (handler is null || keys.Count == 0) return;
        var args = new CacheCleanUpArgs<K>(keys);
        // A faulty handler must not crash the process (Timer callback on a background thread).
        foreach (var d in handler.GetInvocationList())
        {
            if (d is not EventHandler<CacheCleanUpArgs<K>> h) continue;
            try
            {
                h.Invoke(this, args);
            }
            catch (Exception ex)
            {
                Debug.Print(ex.Message);
            }
        }
    }

    /// <summary>
    /// Stops the cleanup timer and releases the cache resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer.Dispose();
    }
    /// <summary>
    /// Removes an entry from the cache.
    /// </summary>
    /// <param name="key">Key to remove.</param>
    /// <returns>True if the entry was removed.</returns>
    public bool Remove(K key)
    {
        bool c;
        lock (_lock)
        {
            c = _cache.Remove(key);
        }
        if (c) OnCacheCleanUp(key);
        return c;
    }
    /// <summary>
    /// Removes the entries with the given keys from the cache.
    /// </summary>
    /// <param name="key">Keys to remove.</param>
    /// <returns>Keys that were actually removed.</returns>
    public List<K> Remove(params IEnumerable<K> key)
    {
        var res = new List<K>();
        lock (_lock)
        {
            foreach (var k in key)
            {
                if (_cache.Remove(k)) res.Add(k);
            }
        }
        if (res.Any()) OnCacheCleanUp(res);
        return res;
    }
    /// <summary>
    /// Forces the expiration of the entries with the given keys.
    /// </summary>
    /// <param name="key">Keys to expire.</param>
    /// <returns>Keys that were expired.</returns>
    public List<K> Expires(params IEnumerable<K> key)
    {
        var res = new List<K>();
        lock (_lock)
        {
            foreach (var k in key)
            {
                // Missing keys (e.g. change tracking INSERTs not yet cached) are ignored.
                if (_cache.TryGetValue(k, out var state) && state is ValidItem<T> vi)
                {
                    if (vi.Value is IDisposable d) d.Dispose();
                    _cache[k] = new ExpiredItem();
                    res.Add(k);
                }
            }
        }
        if (res.Any()) OnCacheCleanUp(res);
        return res;
    }
    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    public void Clear()
    {
        List<K> keys;
        lock (_lock)
        {
            keys = _cache.Keys.ToList();
            _cache.Clear();
        }
        OnCacheCleanUp(keys);
    }
    private void _cleanup(object? state)
    {
        List<K> expiredKeys;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            expiredKeys = new List<K>();
            foreach (var kvp in _cache)
            {
                if (kvp.Value is ValidItem<T> vi && vi.ExpireAt < now)
                {
                    if (vi.Value is IDisposable d) d.Dispose();
                    _cache[kvp.Key] = new ExpiredItem();
                    expiredKeys.Add(kvp.Key);
                }
            }
        }
        OnCacheCleanUp(expiredKeys);
    }
    
    /// <summary>
    /// Ensures the given keys exist in the cache, adding them as expired when missing.
    /// </summary>
    /// <param name="keys">Keys to ensure.</param>
    public void EnsureKeys(params IEnumerable<K> keys)
    {
        lock (_lock)
        {
            foreach (var key in keys)
            {
                if (!_cache.ContainsKey(key)) _cache[key] = new ExpiredItem();
            }
        }
    }
    /// <summary>
    /// Synchronizes the cache key set with the given keys, removing missing keys and adding new ones as expired.
    /// </summary>
    /// <param name="keys">Full key set to keep.</param>
    public void ForceKeys(params IEnumerable<K> keys)
    {
        var newkeys = keys.ToHashSet(_comparer);
        lock (_lock)
        {
            var oldkeys= _cache.Keys.ToHashSet(_comparer);
            foreach (var key in oldkeys.Except(newkeys))
            {
                _cache.Remove(key);
            }
            foreach (var key in newkeys.Except(oldkeys))
            {
                _cache[key] = new ExpiredItem();
            }
        }
    }

    /// <summary>
    /// Returns all cached values, ignoring expiration.
    /// Used as a fallback when a reload fails.
    /// </summary>
    /// <returns>All cached values.</returns>
    public List<T> GetAllValues()
    {
        lock (_lock)
        {
            return _cache.Values
                .Select(v => v is ValidItem<T> vi ? vi.Value : null)
                .Where(v => v is not null)
                .Select(v => v!)
                .ToList();
        }
    }

    /// <summary>
    /// Refreshes the expired entries using the given refresh function, which is invoked outside the cache lock.
    /// </summary>
    /// <param name="refreshfunction">Function that reloads values for the expired keys; it receives the total entry count and the expired key set.</param>
    /// <returns>Result of the refresh operation, or the error that occurred.</returns>
    public EnumerableResult<T> RefreshAll(Func<int,IReadOnlyCollection<K>,EnumerableResult<T>> refreshfunction)
    {
        EnumerableResult<T> final;
        List<K> justexpiredKeys;
        HashSet<K> expiredkeys;
        int count;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            expiredkeys = new HashSet<K>(_comparer);
            justexpiredKeys = new List<K>();
            foreach (var kvp in _cache)
            {
                switch (kvp.Value)
                {
                    case ValidItem<T> vi when vi.ExpireAt < now:
                        if (vi.Value is IDisposable d) d.Dispose();
                        _cache[kvp.Key] = new ExpiredItem();
                        expiredkeys.Add(kvp.Key);
                        justexpiredKeys.Add(kvp.Key);
                        break;
                    case ExpiredItem:
                        expiredkeys.Add(kvp.Key);
                        break;
                    case JustExpiredItem:
                        break; // not stored; defensive
                }
            }
            count = _cache.Count;
        }
        // SD-4: the DB query runs OUTSIDE the lock to avoid blocking readers/writers.
        var res = refreshfunction(count,expiredkeys);
        lock (_lock)
        {
            if (res.IsFailed)
            {
                final = res.Error!;
            }
            else
            {
                foreach (var value in res.Value!)
                {
                    var key = _getKeyFn(value);
                    _cache[key] = new ValidItem<T>(value, DateTime.UtcNow + _defaultExpiration);
                    expiredkeys.Remove(key);
                }
                foreach (var expiredkey in expiredkeys)
                {
                    _cache.Remove(expiredkey);
                }
                justexpiredKeys.Clear();
                final = res;
            }
        }
        OnCacheCleanUp(justexpiredKeys);
        return final;
    }
}