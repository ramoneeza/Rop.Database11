namespace Rop.Database11.TypeHandlers;

/// <summary>
/// Registers the Dapper type handlers for <see cref="DateOnly"/> and <see cref="TimeOnly"/> values.
/// </summary>
public static class NewTypeHandlers
{
    private static int _registered;

    /// <summary>
    /// Registers the type handlers once; subsequent calls are ignored.
    /// </summary>
    public static void Ensure()
    {
        // Interlocked: el registro es unico aunque varios Database se creen en paralelo.
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler2());
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler2());
    }
}