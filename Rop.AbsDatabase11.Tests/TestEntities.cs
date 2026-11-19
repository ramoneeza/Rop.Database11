using Dapper.Contrib.Extensions;

namespace Rop.AbsDatabase11.Tests;

/// <summary>
/// Int key, auto-generated (IDENTITY). Uses Dapper.Contrib [Key].
/// Only used as a generic type argument: the fake connection throws
/// before any SQL runs, so no real table is needed.
/// </summary>
[Table("ItemsIntAuto")]
public class ItemIntAuto
{
    [Key]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int Quantity { get; set; }
}
