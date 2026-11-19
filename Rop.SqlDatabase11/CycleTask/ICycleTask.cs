namespace Rop.Database11.CycleTask;

/// <summary>
/// Contract for a periodic task executed by the cycle service.
/// </summary>
public interface ICycleTask
{
    /// <summary>
    /// Interval between executions.
    /// </summary>
    TimeSpan Interval { get; }
    /// <summary>
    /// Executes the periodic work of the cycle task.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token checked before executing; implementations should throw <see cref="OperationCanceledException"/> when cancelled.</param>
    /// <returns>Error message if the work failed, null on success.</returns>
    string? PayLoad(CancellationToken cancellationToken = default);
}