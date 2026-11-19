using System.Collections;

namespace Rop.Database11
{
    /// <summary>
    /// Change tracking poll result with no visible changed rows.
    /// </summary>
    /// <param name="KeyDescription">Key description of the tracked table.</param>
    /// <param name="Version">New change tracking version after the poll.</param>
    /// <param name="OldVersion">Change tracking version before the poll.</param>
    /// <remarks>
    /// The version may have advanced even without visible changes (for example when
    /// every changed row was filtered out as <see cref="ChangeOperation.Unknown"/>):
    /// consumers must advance their stored version from <see cref="Version"/> regardless
    /// of the emptiness, otherwise the same rows would be re-read forever.
    /// </remarks>
    public sealed record EmptyDelta(KeyDescription KeyDescription, long Version, long OldVersion);

    /// <summary>
    /// Change tracking poll result with one or more changed rows.
    /// </summary>
    public sealed record TableDelta
    {
        /// <summary>
        /// Initializes a table delta with the rows changed since the previous poll.
        /// </summary>
        /// <param name="keyDescription">Key description of the tracked table.</param>
        /// <param name="version">New change tracking version after the poll.</param>
        /// <param name="oldVersion">Change tracking version before the poll.</param>
        /// <param name="changes">Changed rows; stored ordered by operation.</param>
        public TableDelta(KeyDescription keyDescription, long version, long oldVersion, IEnumerable<ChangeRow> changes)
        {
            KeyDescription = keyDescription ?? throw new ArgumentNullException(nameof(keyDescription));
            Version = version;
            OldVersion = oldVersion;
            Changes = (changes ?? throw new ArgumentNullException(nameof(changes))).OrderBy(c => c.Operation).ToList();
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
    }

    /// <summary>
    /// Immutable result of a change tracking poll for a table: either no visible changes
    /// (<see cref="EmptyDelta"/>) or changed rows (<see cref="TableDelta"/>).
    /// U-1: union type that removes the invalid flag combinations of the previous
    /// flags-based model (an empty delta can never hold a changes list, and vice versa).
    /// </summary>
    public union DeltaChanges(EmptyDelta, TableDelta)
    {
        /// <summary>
        /// Key description of the tracked table.
        /// </summary>
        public KeyDescription KeyDescription => this switch
        {
            EmptyDelta e => e.KeyDescription,
            TableDelta t => t.KeyDescription,
        };
        /// <summary>
        /// New change tracking version after the poll.
        /// </summary>
        public long Version => this switch
        {
            EmptyDelta e => e.Version,
            TableDelta t => t.Version,
        };
        /// <summary>
        /// Change tracking version before the poll.
        /// </summary>
        public long OldVersion => this switch
        {
            EmptyDelta e => e.OldVersion,
            TableDelta t => t.OldVersion,
        };
        /// <summary>
        /// Changed rows, ordered by operation (empty for an empty delta).
        /// </summary>
        public IReadOnlyList<ChangeRow> Changes => this switch
        {
            EmptyDelta => [],
            TableDelta t => t.Changes,
        };
        /// <summary>
        /// Indicates whether the delta contains no changes.
        /// </summary>
        public bool IsEmpty => this is EmptyDelta;
        /// <summary>
        /// Indicates whether the poll did not advance the change tracking version.
        /// </summary>
        public bool NoNewVersion => Version == OldVersion;
        /// <summary>
        /// Indicates whether the poll advanced to a new change tracking version.
        /// </summary>
        public bool IsNewVersion => Version > OldVersion;
        /// <summary>
        /// Gets the primary key values of the changed rows.
        /// </summary>
        /// <returns>An enumerable with the primary keys of the changed rows (empty for an empty delta).</returns>
        public IEnumerable GetKeys() => this switch
        {
            EmptyDelta => [],
            TableDelta t => t.Changes.Select(c => c.Id),
        };
        /// <summary>
        /// Gets the primary key values of the changed rows converted to the
        /// specified type (SD-19).
        /// </summary>
        /// <typeparam name="K">Key type.</typeparam>
        /// <returns>An enumerable with the typed primary keys of the changed rows (empty for an empty delta).</returns>
        /// <exception cref="InvalidCastException">Thrown when a key cannot be converted to <typeparamref name="K"/>.</exception>
        public IEnumerable<K> GetKeys<K>() => this switch
        {
            EmptyDelta => [],
            TableDelta t => t.Changes.Select(c => (K)ConvertKeyValue(c.Id, typeof(K))),
        };
        /// <summary>
        /// Gets the key pairs of the changed rows for a partial key table.
        /// </summary>
        /// <returns>An enumerable with the key pairs of the changed rows (empty for an empty delta).</returns>
        /// <exception cref="InvalidOperationException">Thrown when the key description is not a partial key.</exception>
        public IEnumerable<(object, object)> GetPartialKeys()
        {
            EnsurePartial(KeyDescription);
            return this switch
            {
                EmptyDelta => [],
                TableDelta t => t.Changes.Select(c => (c.Id, c.Id2 ?? throw new InvalidOperationException("Not a partial key 2"))),
            };
        }
        /// <summary>
        /// Gets the partial key pairs of the changed rows converted to the
        /// specified types (SD-19).
        /// </summary>
        /// <typeparam name="K1">Type of the first partial key.</typeparam>
        /// <typeparam name="K2">Type of the second partial key.</typeparam>
        /// <returns>An enumerable with the typed partial key pairs of the changed rows (empty for an empty delta).</returns>
        /// <exception cref="InvalidOperationException">Thrown when the key description is not a partial key.</exception>
        /// <exception cref="InvalidCastException">Thrown when a key cannot be converted to the requested type.</exception>
        public IEnumerable<(K1, K2)> GetPartialKeys<K1, K2>()
        {
            EnsurePartial(KeyDescription);
            return this switch
            {
                EmptyDelta => [],
                TableDelta t => t.Changes.Select(c => ((K1)ConvertKeyValue(c.Id, typeof(K1)), (K2)ConvertKeyValue(c.Id2, typeof(K2)))),
            };
        }
        /// <summary>
        /// Type of the key column.
        /// </summary>
        public Type GetKeyType => KeyDescription.KeyType;
        /// <summary>
        /// Type code of the key column.
        /// </summary>
        public TypeCode GetKeyTypeCode => Type.GetTypeCode(GetKeyType);
        /// <summary>
        /// Creates an empty delta (no version advance) for the given key description and version.
        /// </summary>
        /// <param name="kd">Key description of the tracked table.</param>
        /// <param name="lastv">Last known change tracking version.</param>
        /// <returns>Result with an empty delta at the given version.</returns>
        public static Result<DeltaChanges> Empty(KeyDescription kd, long lastv)
        {
            DeltaChanges d = new EmptyDelta(kd, lastv, lastv);
            return d;
        }

        private static void EnsurePartial(KeyDescription kd)
        {
            if (kd is not PartialKeyDescription) throw new InvalidOperationException("Not a partial key");
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
    }
}
