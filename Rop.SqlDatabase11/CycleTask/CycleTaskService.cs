using Microsoft.Extensions.Logging;
using Rop.Database11.Logging;

namespace Rop.Database11.CycleTask;

/// <summary>
/// Class to run tasks in a cycle
/// </summary>
/// <typeparam name="K">Key type used to register the tasks.</typeparam>
/// <typeparam name="T">The task type stored in the cycle queue. Must implement <see cref="ICycleTask"/>.</typeparam>
public class CycleTaskService<K,T>:IDisposable where T: class,ICycleTask where K:notnull
{
    private readonly CycleQueue<T> _queue = new();
    private readonly Dictionary<K,CycleTaskWrapper<T>> _map=new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _lockadd = new();
    private readonly ILogger _logger;
    private Task? _task;
    /// <summary>
    /// Constructs a cycle task service. If no logger is provided, a fallback
    /// writing to Debug.Print is used.
    /// </summary>
    /// <param name="logger">Optional logger for background errors.</param>
    public CycleTaskService(ILogger? logger = null)
    {
        _logger = logger ?? DebugLogger.Instance;
    }
    /// <summary>
    /// Granularity of the pool in milliseconds
    /// </summary>
    public int PoolDelay { get; set; } = 1000;

    /// <summary>
    /// Attempts to add a new task to the queue with the specified key and task creation function.
    /// </summary>
    /// <param name="key">The unique key of the task to be added.</param>
    /// <param name="fntask">A function that creates an instance of the task if it does not already exist.</param>
    /// <param name="interval">Optional interval overriding the task interval when smaller than the current one.</param>
    /// <returns>
    /// The existing task if a task with the specified key already exists;
    /// otherwise, the newly created and added task.
    /// </returns>
    /// <remarks>
    /// If a task with the specified key already exists in the queue, this method will return the existing task 
    /// instead of creating a new one.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="key"/> or <paramref name="fntask"/> is <c>null</c>.
    /// </exception>
    public T TryAdd(K key,Func<K,T> fntask,TimeSpan? interval=null)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (fntask is null) throw new ArgumentNullException(nameof(fntask));
        // SD-5: alta protegida — una sola instancia por clave y un solo bucle de polling.
        lock (_lockadd)
        {
            var final=_map.GetValueOrDefault(key);
            if (final != null)
            {
                if (interval!=null && final.Interval>interval) final.Interval=interval.Value;
                return final.Item;
            }
            var item= fntask(key);
            final=_queue.Enqueue(item);
            _map[key]=final;
            if (_task == null)
            {
                // Start the background task if it's not already running
                _task = Task.Run(() => _runAsync(_cts.Token));
            }
            return item;
        }
    }
    /// <summary>
    /// Last error in the queue
    /// </summary>
    public string? LastError { get; private set; }
    private async Task _runAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PoolDelay, ct);
                if (ct.IsCancellationRequested) break;
                if (!_queue.TryDequeueInTime(out var task)) continue;
                var r = task.Execute(ct);
                if (ct.IsCancellationRequested) break;
                if (r!=null)
                {
                    LastError=r;
                    _logger.LogWarning("Cycle task PayLoad reported an error: {Error}", r);
                }
                _queue.Enqueue(task);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogDebug(ex, "CycleTaskService stopped");
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _logger.LogError(ex, "CycleTaskService background loop error");
            }
        }
    }
    /// <summary>
    /// Stops the polling loop, waits for it to finish and disposes the queued
    /// tasks that implement <see cref="IDisposable"/> (e.g. SqlTableDependency).
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        if (_task != null)
        {
            try
            {
                _task.Wait();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CycleTaskService dispose wait failed");
            }
        }
        _cts.Dispose();
        // Los items IDisposable (p. ej. SqlTableDependency con su canal) se
        // disponen al parar el servicio para no dejar lectores vivos.
        lock (_lockadd)
        {
            foreach (var wrapper in _map.Values)
            {
                if (wrapper.Item is IDisposable d) d.Dispose();
            }
        }
        GC.SuppressFinalize(this);
    }
}