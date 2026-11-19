using Rop.Dapper.ContribEx11;
using Rop.Database11;
using Rop.Database11.Tracking;
using Rop.Results9;

namespace Rop.SqlDatabase11.ChangeTracking.Tests;

/// <summary>
/// DB-less tests for the Change Tracking data types:
/// ChangeRow, DeltaChanges, TrackingVersion, ChangeTrackingPriority
/// and RecordsChangedEventArgs.
/// </summary>
public class ChangeRowAndPriorityTests
{
    private static KeyDescription SingleKey() => DapperHelperExtend.GetKeyDescription<CtItem>();

    private static PartialKeyDescription PartialKey() => DapperHelperExtend.GetPartialKeyDescription<CtPartialItem>();

    // ==== ChangeRow ====

    [Fact]
    public void ChangeRow_Exposes_Id_Id2_And_Operation()
    {
        var row = new ChangeRow(7, "X", ChangeOperation.Update);

        Assert.Equal(7, row.Id);
        Assert.Equal("X", row.Id2);
        Assert.Equal(ChangeOperation.Update, row.Operation);
    }

    // ==== DeltaChanges ====

    [Fact]
    public void DeltaChanges_Sorts_Changes_By_Operation()
    {
        var d = new TableDelta(SingleKey(), 3, 0, new[]
        {
            new ChangeRow(3, null, ChangeOperation.Insert),
            new ChangeRow(1, null, ChangeOperation.Delete),
            new ChangeRow(2, null, ChangeOperation.Update)
        });

        Assert.Equal(
            new[] { ChangeOperation.Delete, ChangeOperation.Update, ChangeOperation.Insert },
            d.Changes.Select(c => c.Operation));
    }

    [Fact]
    public void DeltaChanges_Empty_Is_Empty_And_NoNewVersion()
    {
        var d = DeltaChanges.Empty(SingleKey(), 5).ValueOrThrow();

        Assert.True(d.IsEmpty);
        Assert.True(d.NoNewVersion);
        Assert.False(d.IsNewVersion);
        Assert.Equal(5, d.Version);
        Assert.Equal(5, d.OldVersion);
    }

    [Fact]
    public void DeltaChanges_GetKeys_Returns_Ids()
    {
        DeltaChanges d = new TableDelta(SingleKey(), 2, 0, new[]
        {
            new ChangeRow(1, null, ChangeOperation.Insert),
            new ChangeRow(2, null, ChangeOperation.Insert)
        });

        Assert.Equal(new object[] { 1, 2 }, d.GetKeys().Cast<object>().ToArray());
    }

    [Fact]
    public void DeltaChanges_GetPartialKeys_Throws_On_Single_Key()
    {
        DeltaChanges d = new TableDelta(SingleKey(), 1, 0, new[] { new ChangeRow(1, null, ChangeOperation.Insert) });

        Assert.Throws<InvalidOperationException>(() => d.GetPartialKeys().ToList());
    }

    [Fact]
    public void DeltaChanges_GetPartialKeys_Returns_Tuples()
    {
        DeltaChanges d = new TableDelta(PartialKey(), 1, 0, new[] { new ChangeRow(1, "A", ChangeOperation.Insert) });

        var keys = d.GetPartialKeys().ToList();

        Assert.Contains(((object)1, (object)"A"), keys);
    }

    [Fact]
    public void DeltaChanges_KeyType_Info()
    {
        DeltaChanges d = new TableDelta(SingleKey(), 1, 0, new[] { new ChangeRow(1, null, ChangeOperation.Insert) });

        Assert.Equal(typeof(int), d.GetKeyType);
        Assert.Equal(TypeCode.Int32, d.GetKeyTypeCode);
    }

    [Fact]
    public void DeltaChanges_Can_Be_Empty_With_New_Version()
    {
        // SD-7/U-1: el estado "version nueva pero sin cambios" es posible (todas las
        // operaciones filtradas como Unknown). Se modela como EmptyDelta con
        // version nueva; PayLoad debe avanzar TableVersion en este caso.
        DeltaChanges d = new EmptyDelta(SingleKey(), 5, 0);

        Assert.True(d.IsEmpty);
        Assert.True(d.IsNewVersion);
        Assert.False(d.NoNewVersion);
    }

    // ==== ChangeTrackingPriority ====

    [Fact]
    public void Priority_ToInterval_Is_Power_Of_Two_Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), ChangeTrackingPriority.Realtime.ToInterval());
        Assert.Equal(TimeSpan.FromSeconds(16), ChangeTrackingPriority.Medium.ToInterval());
        Assert.Equal(TimeSpan.FromSeconds(1024), ChangeTrackingPriority.Lowest.ToInterval());
    }

    [Fact]
    public void Priority_ToSeconds_Matches()
    {
        Assert.Equal(1, ChangeTrackingPriority.Realtime.ToSeconds());
        Assert.Equal(8, ChangeTrackingPriority.MediumHigh.ToSeconds());
        Assert.Equal(256, ChangeTrackingPriority.Minimal.ToSeconds());
    }

    [Fact]
    public void Priority_GetDescription_Is_Not_Empty_For_All_Values()
    {
        foreach (var p in Enum.GetValues<ChangeTrackingPriority>())
            Assert.False(string.IsNullOrEmpty(p.GetDescription()));

        Assert.Contains("Realtime", ChangeTrackingPriority.Realtime.GetDescription());
        Assert.Contains("Default", ChangeTrackingPriority.Default.GetDescription());
    }

    [Fact]
    public void Priority_Descriptions_Are_Consistent()
    {
        // SD-16: MediumHigh no es el default; Default es alias de Medium.
        Assert.DoesNotContain("Default", ChangeTrackingPriority.MediumHigh.GetDescription());
        Assert.Equal(
            ChangeTrackingPriority.Medium.GetDescription(),
            ChangeTrackingPriority.Default.GetDescription());
    }

    // ==== TrackingVersion ====

    [Fact]
    public void TrackingVersion_MinValue_And_Ticks()
    {
        Assert.Equal(0, TrackingVersion.MinValue.Version);

        var now = DateTimeOffset.Now;
        var tv = new TrackingVersion { Version = 5, Timestamp = now };
        Assert.Equal(5, tv.Version);
        Assert.Equal(now.Ticks, tv.Ticks);
    }

    [Fact]
    public void DeltaChanges_GetKeys_Generic_Returns_Typed_Keys()
    {
        // SD-19: acceso tipado sin casts en el consumidor.
        DeltaChanges d = new TableDelta(SingleKey(), 2, 0, new[]
        {
            new ChangeRow(1, null, ChangeOperation.Insert),
            new ChangeRow(2, null, ChangeOperation.Insert)
        });

        Assert.Equal(new[] { 1, 2 }, d.GetKeys<int>().ToArray());
    }

    [Fact]
    public void DeltaChanges_GetKeys_Generic_Wrong_Type_Throws()
    {
        // int -> string seria convertible via IConvertible; int -> Guid no.
        DeltaChanges d = new TableDelta(SingleKey(), 1, 0, new[] { new ChangeRow(1, null, ChangeOperation.Insert) });

        Assert.Throws<InvalidCastException>(() => d.GetKeys<Guid>().ToList());
    }

    [Fact]
    public void DeltaChanges_GetPartialKeys_Generic_Returns_Typed_Tuples()
    {
        DeltaChanges d = new TableDelta(PartialKey(), 1, 0, new[] { new ChangeRow(1, "A", ChangeOperation.Insert) });

        var keys = d.GetPartialKeys<int, string>().ToList();

        Assert.Contains((1, "A"), keys);
    }

    // ==== RecordsChangedEventArgs ====

    [Fact]
    public void RecordsChangedEventArgs_Null_Means_Full_Reload()
    {
        // SD-19: Reloaded renombrado a IsFullReload (la recarga completa no
        // significa "sin cambios", significa "todos").
        var e = new RecordsChangedEventArgs<int>(null);

        Assert.Empty(e.Changes);
        Assert.True(e.IsFullReload);
    }

    [Fact]
    public void RecordsChangedEventArgs_With_Changes_Is_Not_Full_Reload()
    {
        var e = new RecordsChangedEventArgs<int>(new[] { 1, 2 });

        Assert.False(e.IsFullReload);
        Assert.Equal(2, e.Changes.Count);
    }
}
