using System.Data.Common;

namespace Rop.Database;
internal static class ConnStringExtractor
{
    /// <summary>
    /// Extrae DataSource e InitialCatalog como tupla. Devuelve null en cada elemento si no se encuentra.
    /// Reconoce los alias habituales de SQL Server: Data Source/Server/Address/Addr/Network Address
    /// e Initial Catalog/Database. Los valores entre comillas (p. ej. passwords con ';' o con texto
    /// que parece una clave) se respetan gracias a DbConnectionStringBuilder.
    /// </summary>
    public static (string? DataSource, string? InitialCatalog) Extract(string? conn)
    {
        if (string.IsNullOrEmpty(conn)) return (null, null);
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = conn };
            var dataSource = GetFirst(builder, "Data Source", "Server", "Address", "Addr", "Network Address");
            var initialCatalog = GetFirst(builder, "Initial Catalog", "Database");
            return (dataSource, initialCatalog);
        }
        catch (ArgumentException)
        {
            // Connection string malformado: sin datos extraibles.
            return (null, null);
        }
    }

    private static string? GetFirst(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value is not null)
            {
                var s = value.ToString();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        return null;
    }
}
