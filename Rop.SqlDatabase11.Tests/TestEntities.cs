using Dapper.Contrib.Extensions;
using Rop.Dapper.ContribEx11;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Int key, auto-generated (IDENTITY). Uses Dapper.Contrib [Key].
/// </summary>
[Table("ItemsIntAuto")]
public class ItemIntAuto
{
    [Key]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// String key, assigned manually. String keys must use [ExplicitKey].
/// </summary>
[Table("ItemsStringKey")]
public class ItemStringKey
{
    [ExplicitKey]
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// Entity with no key at all. Only usable with InsertNoKeyAtt / GetAllNoKey.
/// </summary>
[Table("LogEntries")]
public class LogEntry
{
    public string Message { get; set; } = "";

    public int Severity { get; set; }
}

/// <summary>
/// Entity without a main key but with two [PartialKey] properties.
/// The [Computed] property is excluded from InsertNoKeyAtt inserts.
/// </summary>
[Table("ItemsPartial")]
public class ItemPartial
{
    [PartialKey(0)]
    public int OwnerId { get; set; }

    [PartialKey(1)]
    public string Code { get; set; } = "";

    public int Quantity { get; set; }

    [Computed]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Main entity for QueryJoin tests. Int auto key.
/// </summary>
[Table("Orders")]
public class Order
{
    [Key]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    [Computed]
    public List<OrderLine> Lines { get; set; } = [];
}

/// <summary>
/// Joined entity for QueryJoin tests. Manual int key.
/// Deliberately named "Id" like the main entity: Dapper resolves the
/// multi-mapping split right-to-left, so repeated names are supported.
/// </summary>
[Table("OrderLines")]
public class OrderLine
{
    [ExplicitKey]
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string Product { get; set; } = "";
}

/// <summary>
/// CE-7: table with a reserved T-SQL word ('Order') and reserved column
/// ('User'), for Check<T> and wrappers with escaping.
/// </summary>
[Table("Order")]
public class ReservedOrderItem
{
    [ExplicitKey]
    public int Id { get; set; }

    public string User { get; set; } = "";

    public int Quantity { get; set; }
}
