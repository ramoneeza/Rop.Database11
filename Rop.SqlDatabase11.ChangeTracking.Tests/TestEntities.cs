using Dapper.Contrib.Extensions;
using Rop.Dapper.ContribEx11;

namespace Rop.SqlDatabase11.ChangeTracking.Tests;

/// <summary>
/// Single int key entity for Change Tracking tests.
/// </summary>
[Table("CtItems")]
public class CtItem
{
    [Key]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// Entity with two partial keys for Change Tracking tests.
/// </summary>
[Table("CtPartial")]
public class CtPartialItem
{
    [PartialKey(0)]
    public int OwnerId { get; set; }

    [PartialKey(1)]
    public string Code { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// Entity whose table intentionally has Change Tracking disabled.
/// Used to verify the failure paths of VerifyNotify/GetTableChanges.
/// </summary>
[Table("CtNoTrack")]
public class CtNoTrackItem
{
    [ExplicitKey]
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>
/// Entity with DateOnly/TimeOnly columns (nullable variants included)
/// to exercise the registered Dapper type handlers.
/// </summary>
[Table("CtTypes")]
public class CtTypeRow
{
    [Key]
    public int Id { get; set; }

    public DateOnly D { get; set; }

    public DateOnly? D2 { get; set; }

    public TimeOnly T { get; set; }

    public TimeOnly? T2 { get; set; }
}

/// <summary>
/// CE-7: tabla con palabra reservada ('Order') para validar el escapado en
/// 'changetable(changes [Order], ...)'.
/// </summary>
[Table("Order")]
public class CtOrderItem
{
    [ExplicitKey]
    public int Id { get; set; }

    public string User { get; set; } = "";

    public int Quantity { get; set; }
}
