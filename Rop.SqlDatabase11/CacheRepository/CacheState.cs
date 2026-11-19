namespace Rop.Database11.CacheRepository;

/// <summary>
/// Cache state for a valid, non-expired entry.
/// </summary>
/// <typeparam name="T">Type of the cached value.</typeparam>
/// <param name="Value">Cached value.</param>
/// <param name="ExpireAt">UTC time at which the entry expires.</param>
public sealed record ValidItem<T>(T Value, DateTime ExpireAt);

/// <summary>
/// Cache state for an entry that was already expired (no value available).
/// </summary>
public sealed record ExpiredItem;

/// <summary>
/// Cache state for an entry that expired on the current access
/// (its value has been disposed and removed).
/// </summary>
public sealed record JustExpiredItem;

/// <summary>
/// State of a cached entry: valid with value, expired on this access, or already expired.
/// U-2: union type that removes the invalid flag combinations of the previous
/// <c>CacheItem&lt;T&gt;</c> model (a valid state always carries a value;
/// expired states never do).
/// </summary>
/// <typeparam name="T">Type of the cached value.</typeparam>
public union CacheState<T>(ValidItem<T>, JustExpiredItem, ExpiredItem)
{
    /// <summary>
    /// Indicates whether the entry is expired.
    /// </summary>
    public bool IsExpired => this is not ValidItem<T>;
    /// <summary>
    /// Indicates whether the entry expired on this access.
    /// </summary>
    public bool IsJustExpired => this is JustExpiredItem;
}
