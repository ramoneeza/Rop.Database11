namespace Rop.Database11
{
    /// <summary>
    /// Represents a single row change reported by SQL Server change tracking.
    /// </summary>
    /// <param name="id">Primary key value of the changed row.</param>
    /// <param name="id2">Second key value for partial keys, or null.</param>
    /// <param name="operation">Operation that affected the row.</param>
    public readonly struct ChangeRow(object id,object? id2, ChangeOperation operation)
    {
        /// <summary>
        /// Primary key value of the changed row.
        /// </summary>
        public object Id { get; } = id;
        /// <summary>
        /// Second key value for partial keys.
        /// </summary>
        public object? Id2 { get; } = id2;
        /// <summary>
        /// Operation that affected the row.
        /// </summary>
        public ChangeOperation Operation { get; } = operation;
    }
    /// <summary>
    /// Operation performed on a tracked row.
    /// </summary>
    public enum ChangeOperation
    {
        /// <summary>
        /// Row was deleted.
        /// </summary>
        Delete,
        /// <summary>
        /// Row was updated.
        /// </summary>
        Update,
        /// <summary>
        /// Row was inserted.
        /// </summary>
        Insert,
        /// <summary>
        /// Operation could not be determined.
        /// </summary>
        Unknown
    }
}