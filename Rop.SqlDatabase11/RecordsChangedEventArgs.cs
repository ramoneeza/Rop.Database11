namespace Rop.Database11
{
    /// <summary>
    /// Event arguments for records that are changed.
    /// </summary>
    /// <typeparam name="K">Key type</typeparam>
    /// <param name="changes">Keys of the changed records, or null for a full reload.</param>
    public class RecordsChangedEventArgs<K>(IReadOnlyCollection<K>? changes) : EventArgs
    {
        /// <summary>
        /// Gets the keys of the changed records.
        /// </summary>
        public readonly IReadOnlyCollection<K> Changes = changes??new List<K>();
        /// <summary>
        /// Gets a value indicating whether the event corresponds to a full reload
        /// (no specific keys reported). SD-19: renamed from Reloaded.
        /// </summary>
        /// <returns>True if no specific keys are reported; otherwise false.</returns>
        public bool IsFullReload { get; }=(changes?.Count??0)==0;
    }
}