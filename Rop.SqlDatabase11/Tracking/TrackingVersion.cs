namespace Rop.Database11.Tracking;
/// <summary>
/// Change tracking version with the timestamp of the last poll.
/// </summary>
public readonly record struct TrackingVersion
{
    /// <summary>
    /// Current change tracking version of the database (SYS_CHANGE_VERSION).
    /// </summary>
    public required long Version { get; init; }
    /// <summary>
    /// Time at which the version was read.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
    /// <summary>
    /// Ticks of the timestamp.
    /// </summary>
    public long Ticks => Timestamp.Ticks;
    /// <summary>
    /// Minimum possible tracking version.
    /// </summary>
    public static TrackingVersion MinValue => new TrackingVersion { Version = 0, Timestamp = DateTimeOffset.MinValue };
}
