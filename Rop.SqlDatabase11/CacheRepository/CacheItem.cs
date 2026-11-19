namespace Rop.Database11.CacheRepository;

/// <summary>
/// Immutable snapshot of a cached value together with its expiration state.
/// </summary>
/// <typeparam name="T">Type of the cached value.</typeparam>
/// <param name="Expired">Indicates whether the entry is expired.</param>
/// <param name="ExpiredNow">Indicates whether the entry expired on this access.</param>
/// <param name="Value">Cached value, or null when expired or missing.</param>
public record CacheItem<T>(bool Expired, bool ExpiredNow, T? Value)
{
    /// <summary>
    /// Indicates whether the entry is expired.
    /// </summary>
    public bool IsExpired => Expired;
    /// <summary>
    /// Indicates whether the entry expired on this access.
    /// </summary>
    public bool IsJustExpired => ExpiredNow;
    /// <summary>
    /// Item for an entry that expired on this access.
    /// </summary>
    public static CacheItem<T> JustExpiredItem => new(true, true, default);
    /// <summary>
    /// Item for an entry that was already expired.
    /// </summary>
    public static CacheItem<T> ExpiredItem => new(true, false, default);
    /// <summary>
    /// Creates an item for a valid, non-expired value.
    /// </summary>
    /// <param name="value">Cached value.</param>
    /// <returns>Non-expired item.</returns>
    public static CacheItem<T> NotExpired(T value) => new(false, false, value);
}