using System.Collections;

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
    /// Immutable result of a change tracking poll for a table.
    /// </summary>
    public record DeltaChanges
    {
        /// <summary>
        /// Initializes a new change tracking delta.
        /// </summary>
        /// <param name="keyDescription">Key description of the tracked table.</param>
        /// <param name="version">New change tracking version after the poll.</param>
        /// <param name="oldVersion">Change tracking version before the poll.</param>
        /// <param name="changes">Changed rows; stored ordered by operation.</param>
        public DeltaChanges(KeyDescription keyDescription, long version, long oldVersion, IEnumerable<ChangeRow> changes)
        {
            KeyDescription = keyDescription ?? throw new ArgumentNullException(nameof(keyDescription));
            Version= version;
            OldVersion = oldVersion;
            Changes = changes?.OrderBy(c=>c.Operation).ToList() ?? throw new ArgumentNullException(nameof(changes));
        }
        /// <summary>
        /// Key description of the tracked table.
        /// </summary>
        public KeyDescription KeyDescription { get; }
        /// <summary>
        /// New change tracking version after the poll.
        /// </summary>
        public long Version { get; }
        /// <summary>
        /// Change tracking version before the poll.
        /// </summary>
        public long OldVersion { get; }
        /// <summary>
        /// Changed rows, ordered by operation.
        /// </summary>
        public IReadOnlyList<ChangeRow> Changes { get; }
        /// <summary>
        /// Gets the primary key values of the changed rows.
        /// </summary>
        /// <returns>An enumerable with the primary keys of the changed rows.</returns>
        public IEnumerable GetKeys()=>Changes.Select(c => c.Id);

        /// <summary>
        /// Gets the primary key values of the changed rows converted to the
        /// specified type (SD-19).
        /// </summary>
        /// <typeparam name="K">Key type.</typeparam>
        /// <returns>An enumerable with the typed primary keys of the changed rows.</returns>
        /// <exception cref="InvalidCastException">Thrown when a key cannot be converted to <typeparamref name="K"/>.</exception>
        public IEnumerable<K> GetKeys<K>()=>Changes.Select(c => (K)ConvertKeyValue(c.Id, typeof(K)));

        /// <summary>
        /// Gets the key pairs of the changed rows for a partial key table.
        /// </summary>
        /// <returns>An enumerable with the key pairs of the changed rows.</returns>
        public IEnumerable<(object, object)> GetPartialKeys()
        {
            if (KeyDescription is not PartialKeyDescription pkd) throw new InvalidOperationException("Not a partial key");
            return Changes.Select(c => (c.Id, c.Id2 ?? throw new InvalidOperationException("Not a partial key 2")));
        }

        /// <summary>
        /// Gets the partial key pairs of the changed rows converted to the
        /// specified types (SD-19).
        /// </summary>
        /// <typeparam name="K1">Type of the first partial key.</typeparam>
        /// <typeparam name="K2">Type of the second partial key.</typeparam>
        /// <returns>An enumerable with the typed partial key pairs of the changed rows.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the key description is not a partial key.</exception>
        /// <exception cref="InvalidCastException">Thrown when a key cannot be converted to the requested type.</exception>
        public IEnumerable<(K1, K2)> GetPartialKeys<K1, K2>()
        {
            if (KeyDescription is not PartialKeyDescription) throw new InvalidOperationException("Not a partial key");
            return Changes.Select(c => ((K1)ConvertKeyValue(c.Id, typeof(K1)), (K2)ConvertKeyValue(c.Id2, typeof(K2))));
        }

        /// <summary>
        /// Converts a raw key value (as mapped by Dapper) to the target key type.
        /// </summary>
        /// <param name="value">Raw key value.</param>
        /// <param name="type">Target key type.</param>
        /// <returns>Key value converted to the target type.</returns>
        /// <exception cref="InvalidCastException">Thrown when the value cannot be converted.</exception>
        internal static object ConvertKeyValue(object? value, Type type)
        {
            if (value is null) throw new InvalidCastException("Cannot convert a null key value");
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t.IsInstanceOfType(value)) return value;
            try
            {
                if (t == typeof(Guid)) return Guid.Parse(value.ToString()!);
                if (t.IsEnum) return Enum.Parse(t, value.ToString()!);
                return Convert.ChangeType(value, t, System.Globalization.CultureInfo.InvariantCulture)!;
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
            {
                // Contrato uniforme: cualquier fallo de conversion sale como
                // InvalidCastException (con la causa como inner exception).
                throw new InvalidCastException($"Cannot convert key value '{value}' to type {t.Name}", ex);
            }
        }
        /// <summary>
        /// Type of the key column.
        /// </summary>
        public Type GetKeyType => KeyDescription.KeyType;
        /// <summary>
        /// Type code of the key column.
        /// </summary>
        public TypeCode GetKeyTypeCode=>Type.GetTypeCode(GetKeyType);
        /// <summary>
        /// Indicates whether the delta contains no changes.
        /// </summary>
        public bool IsEmpty=> Changes.Count == 0;
        /// <summary>
        /// Indicates whether the poll did not advance the change tracking version.
        /// </summary>
        public bool NoNewVersion=> Version == OldVersion;
        /// <summary>
        /// Indicates whether the poll advanced to a new change tracking version.
        /// </summary>
        public bool IsNewVersion=> Version > OldVersion;
        /// <summary>
        /// Creates an empty delta for the given key description and version.
        /// </summary>
        /// <param name="kd">Key description of the tracked table.</param>
        /// <param name="lastv">Last known change tracking version.</param>
        /// <returns>Result with an empty delta at the given version.</returns>
        public static Result<DeltaChanges> Empty(KeyDescription kd, long lastv)
        {
            return new DeltaChanges(kd,lastv, lastv, new List<ChangeRow>());
        }
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