namespace Rop.Database11.CacheRepository;

/// <summary>
/// Event arguments with the keys cleaned up from a <see cref="MemoryCache{K, T}"/>.
/// </summary>
/// <typeparam name="K">Type of the cache key.</typeparam>
public class CacheCleanUpArgs<K> : EventArgs where K : notnull
{
    /// <summary>
    /// Keys cleaned up from the cache.
    /// </summary>
    public IReadOnlyCollection<K> CleanUpKeys { get; }
    /// <summary>
    /// Initializes the event arguments.
    /// </summary>
    /// <param name="keys">Keys cleaned up from the cache.</param>
    public CacheCleanUpArgs(IReadOnlyCollection<K> keys)
    {
        CleanUpKeys = keys;
    }
}