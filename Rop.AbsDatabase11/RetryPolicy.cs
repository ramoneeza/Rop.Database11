namespace Rop.Database;

/// <summary>
/// Retry policy for transient database errors
/// (deadlocks 1205, timeouts -2, connection/network failures, transient Azure SQL errors...).
/// </summary>
/// <remarks>
/// Retries run the whole operation with a NEW connection (and, on the transactional
/// paths, a new transaction). Business errors returned as a failed
/// <c>Result</c> are NOT retried: only transient exceptions are retried.
/// Retries apply to all <see cref="AbsDatabase"/> operations
/// (UnitOfWork, Query, Execute, Insert/Update/Delete...) except queries that open
/// their own connection outside that layer (e.g. the change tracking polling, which
/// self-heals on the next cycle).
/// </remarks>
public sealed record RetryPolicy
{
    /// <summary>
    /// Default policy: 3 retries with 100, 250 and 500 ms backoff and
    /// detection of the standard SQL Server transient errors.
    /// </summary>
    public static RetryPolicy Default { get; } = new();

    /// <summary>
    /// Policy with no retries (a single attempt).
    /// </summary>
    public static RetryPolicy NoRetry { get; } = new() { MaxRetries = 0 };

    /// <summary>
    /// Maximum number of retries after the first attempt (0 = no retries). Default 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Delay before each retry (one per retry, in order).
    /// If there are more retries than delays, the last one is reused. Default 100, 250 and 500 ms.
    /// </summary>
    public IReadOnlyList<TimeSpan> Delays { get; init; } =
        [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)];

    /// <summary>
    /// Predicate that decides whether an exception is transient and worth retrying.
    /// If <c>null</c>, the standard detector is used (<see cref="IsTransientDefault"/>).
    /// </summary>
    public Func<Exception, bool>? IsTransient { get; init; }

    /// <summary>
    /// Classifies an exception as transient (without taking the attempt count into account).
    /// </summary>
    internal bool IsTransientError(Exception ex) => IsTransient is not null ? IsTransient(ex) : IsTransientDefault(ex);

    /// <summary>
    /// Returns whether failed attempt number <paramref name="attempt"/> (1-based)
    /// is worth retrying under this policy.
    /// </summary>
    /// <param name="ex">Exception from the failed attempt.</param>
    /// <param name="attempt">Number of the failed attempt (1 = the first).</param>
    internal bool CanRetry(Exception ex, int attempt)
    {
        if (attempt > MaxRetries) return false;
        return IsTransientError(ex);
    }

    /// <summary>
    /// Delay to apply after failed attempt number <paramref name="attempt"/>.
    /// </summary>
    internal TimeSpan DelayFor(int attempt)
    {
        if (Delays.Count == 0) return TimeSpan.Zero;
        return Delays[Math.Min(attempt - 1, Delays.Count - 1)];
    }

    /// <summary>
    /// Standard detector of SQL Server transient errors (works without referencing
    /// Microsoft.Data.SqlClient: detects <c>SqlException</c> via reflection).
    /// </summary>
    /// <param name="ex">Exception to classify.</param>
    /// <returns><c>true</c> if the exception is a retryable transient error.</returns>
    public static bool IsTransientDefault(Exception ex)
    {
        if (ex is TimeoutException) return true;
        if (ex is AggregateException ag) return ag.InnerExceptions.Any(IsTransientDefault);
        var type = ex.GetType();
        var name = type.FullName;
        if (name is not ("Microsoft.Data.SqlClient.SqlException" or "System.Data.SqlClient.SqlException")) return false;
        var numberProp = type.GetProperty("Number");
        var number = numberProp?.GetValue(ex) is int n ? n : 0;
        // 1205 deadlock | -2 timeout | 2/53/64/20/233 connection | 10053/10054/10060 winsock |
        // 4060 database unavailable | 40197/40501/40540/40613/10928/10929/40627/4221/615 Azure SQL
        if (number is 1205 or -2 or 2 or 53 or 64 or 20 or 233 or 4060
            or 10053 or 10054 or 10060
            or 40197 or 40501 or 40540 or 40613 or 10928 or 10929 or 40627 or 4221 or 615) return true;
        var isTransientProp = type.GetProperty("IsTransient");
        return isTransientProp?.GetValue(ex) is true;
    }
}
