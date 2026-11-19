namespace Rop.Database11.CycleTask;

/// <summary>
/// Wraps a cycle task with its scheduling state and executes it on its interval using UTC times.
/// </summary>
/// <typeparam name="T">Cycle task type.</typeparam>
public class CycleTaskWrapper<T> where T: ICycleTask
{
    /// <summary>
    /// Wrapped cycle task.
    /// </summary>
    public T Item { get; set; }
    /// <summary>
    /// Interval between executions.
    /// </summary>
    public TimeSpan Interval { get; set; }
    /// <summary>
    /// UTC time of the last execution.
    /// </summary>
    public DateTimeOffset LastRun { get; private set; } = DateTimeOffset.MinValue;
    /// <summary>
    /// UTC time of the next scheduled execution.
    /// </summary>
    public DateTimeOffset NextRun { get; private set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Duration of the last execution.
    /// </summary>
    public TimeSpan Delay { get; private set; }

    /// <summary>
    /// Initializes a wrapper for the given cycle task.
    /// </summary>
    /// <param name="item">Cycle task to wrap.</param>
    public CycleTaskWrapper(T item)
    {
        Item=item;
        Interval=item.Interval;
    }
    /// <summary>
    /// Executes the wrapped task when its next run time is due and reschedules it.
    /// </summary>
    /// <param name="ct">Optional cancellation token checked before executing.</param>
    /// <returns>A <see cref="VoidResult"/> indicating success (executed or not yet due) or the failure details.</returns>
    public VoidResult Execute(CancellationToken? ct = null)
    {
        if (ct?.IsCancellationRequested ?? false) return VoidResult.Ok;
        // SD-14: instantes UTC en todo el scheduler (antes se mezclaban
        // DateTimeOffset.Now y DateTime.Now).
        var now = DateTimeOffset.UtcNow;
        if (now < NextRun) return VoidResult.Ok;
        LastRun = now;
        try
        {
            return Item.PayLoad(ct ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // La cancelacion sube al bucle del CycleTaskService, que la usa
            // para detenerse de forma limpia.
            throw;
        }
        catch (Exception ex)
        {
            return Error.Exception(ex);
        }
        finally
        {
            var end = DateTimeOffset.UtcNow;
            Delay = end - now;
            NextRun = end + Interval;
        }
    }
}