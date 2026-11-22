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
    /// <returns>A <see cref="VoidResult"/> indicating success or the failure details (CH-1: typed operational error with Result, instead of the "null = success" convention).</returns>
    VoidResult PayLoad(CancellationToken cancellationToken = default);
}