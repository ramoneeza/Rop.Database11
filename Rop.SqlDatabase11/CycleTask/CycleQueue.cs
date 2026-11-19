using System.Diagnostics.CodeAnalysis;

namespace Rop.Database11.CycleTask;

/// <summary>
/// Priority queue of cycle tasks ordered by their next run time.
/// </summary>
/// <typeparam name="T">Cycle task type.</typeparam>
public class CycleQueue<T> where T : class,ICycleTask
{
    private readonly PriorityQueueDateTime _queue = new();
    private readonly Lock _lock = new();
    private class PriorityQueueDateTime : PriorityQueue<CycleTaskWrapper<T>, DateTimeOffset> { }
    /// <summary>
    /// Clear the queue
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
        }
    }
    /// <summary>
    /// Enqueue a CycleTask
    /// </summary>
    /// <param name="element">Cycle task to enqueue.</param>
    /// <returns>Wrapper created for the enqueued task.</returns>
    public CycleTaskWrapper<T> Enqueue(T element)
    {
        var wrapper = new CycleTaskWrapper<T>(element);
        Enqueue(wrapper);
        return wrapper;
    }

    /// <summary>
    /// Enqueue a wrapper in the queue.
    /// </summary>
    /// <param name="element">Wrapper to enqueue.</param>
    public void Enqueue(CycleTaskWrapper<T> element)
    {
        lock (_lock)
        {
            _queue.Enqueue(element,element.NextRun);
        }
    }

    /// <summary>
    /// Try to dequeue a CycleTask
    /// </summary>
    /// <param name="element">When successful, the dequeued wrapper.</param>
    /// <returns>True when a wrapper was dequeued, false when the queue was empty.</returns>
    public bool TryDequeue([MaybeNullWhen(false)] out CycleTaskWrapper<T> element)
    {
        lock (_lock)
        {
            return _queue.TryDequeue(out element, out _);
        }
    }
    /// <summary>
    /// Try to dequeue a CycleTask in time
    /// </summary>
    /// <param name="element">When successful, the dequeued wrapper.</param>
    /// <returns>True when a due wrapper was dequeued, false when the queue was empty or the next wrapper is not due yet.</returns>
    public bool TryDequeueInTime([MaybeNullWhen(false)] out CycleTaskWrapper<T> element)
    {
        lock (_lock)
        {
            element = null;
            if (!_queue.TryPeek(out var wrapper, out _)) return false;
            // SD-14: comparacion UTC consistente (antes DateTime.Now convertido
            // implicitamente a DateTimeOffset con el offset local).
            if (wrapper.NextRun > DateTimeOffset.UtcNow) return false;
            element = _queue.Dequeue();
            return true;
        }
    }
    /// <summary>
    /// Try to peek a CycleTask
    /// </summary>
    /// <param name="element">When successful, the next wrapper without removing it.</param>
    /// <returns>True when the queue is not empty, false otherwise.</returns>

    public bool TryPeek([MaybeNullWhen(false)] out CycleTaskWrapper<T> element)
    {
        lock (_lock)
        {
            return _queue.TryPeek(out element, out _);
        }
    }
    /// <summary>
    /// Get the count of the queue
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// Gets the first cycle task matching the predicate.
    /// </summary>
    /// <param name="predicate">Condition to match.</param>
    /// <returns>Matching cycle task or null when none matches.</returns>
    public T? FirstOrDefault(Func<T, bool> predicate)
    {
        lock (_lock)
        {
            var item = _queue.UnorderedItems.Select(x=>x.Element).FirstOrDefault(x=>predicate(x.Item));
            return item?.Item;
        }
    }
    /// <summary>
    /// Removes the wrapper of the specified cycle task.
    /// </summary>
    /// <param name="item">Cycle task to remove.</param>
    /// <returns>True when the task was found and removed, false otherwise.</returns>
    public bool Remove(T item)
    {
        lock (_lock)
        {
            var wrapper = _queue.UnorderedItems.Select(x=>x.Element).FirstOrDefault(x => x.Item == item);
            if (wrapper != null)
            {
                _queue.Remove(wrapper,out _,out _);
                return true;
            }
            return false;
        }
    }
}