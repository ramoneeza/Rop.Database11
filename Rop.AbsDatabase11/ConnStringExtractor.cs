using System.Data.Common;

namespace Rop.Database;
internal static class ConnStringExtractor
{
    /// <summary>
    /// Extracts DataSource and InitialCatalog as a tuple. Returns null for each element if not found.
    /// Recognizes the usual SQL Server aliases: Data Source/Server/Address/Addr/Network Address
    /// and Initial Catalog/Database. Quoted values (e.g. passwords with ';' or with text
    /// that looks like a key) are respected thanks to DbConnectionStringBuilder.
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
            // Malformed connection string: no extractable data.
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
