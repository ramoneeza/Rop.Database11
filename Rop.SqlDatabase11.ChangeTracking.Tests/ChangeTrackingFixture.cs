using Dapper;
using Microsoft.Data.SqlClient;

[assembly: AssemblyFixture(typeof(Rop.SqlDatabase11.ChangeTracking.Tests.ChangeTrackingFixture))]

namespace Rop.SqlDatabase11.ChangeTracking.Tests;

/// <summary>
/// Creates an isolated SQL Server database with Change Tracking enabled
/// at database level. Each test enables CT on its own tables.
///
/// Requisitos de Change Tracking (Rop.Database11):
/// - BASE DE DATOS: CHANGE_TRACKING = ON con retencion minima de 2 dias
///   y limpieza automatica (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON).
/// - TABLA: ENABLE CHANGE_TRACKING a nivel de tabla. El seguimiento de
///   columnas (TRACK_COLUMNS_UPDATED) puede estar en false.
/// </summary>
public sealed class ChangeTrackingFixture : IAsyncLifetime
{
    private bool _databaseCreated;

    public ChangeTrackingFixture()
    {
        DatabaseName = $"RopSqlDb11CtTest_{Guid.NewGuid():N}";
        var configuredConnection = Environment.GetEnvironmentVariable("ROPDB_TEST_CONNECTION_STRING");
        var builder = configuredConnection is null
            ? new SqlConnectionStringBuilder
            {
                DataSource = Environment.GetEnvironmentVariable("ROPDB_TEST_SERVER") ?? @"(localdb)\MSSQLLocalDB",
                IntegratedSecurity = true,
                TrustServerCertificate = true
            }
            : new SqlConnectionStringBuilder(configuredConnection);

        builder.InitialCatalog = "master";
        builder.Pooling = false;
        MasterConnectionString = builder.ConnectionString;
        builder.InitialCatalog = DatabaseName;
        ConnectionString = builder.ConnectionString;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public string MasterConnectionString { get; }

    public async ValueTask InitializeAsync()
    {
        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync();
        await master.ExecuteAsync($"CREATE DATABASE [{DatabaseName}]");
        _databaseCreated = true;
        await master.ExecuteAsync($"""
            ALTER DATABASE [{DatabaseName}] SET CHANGE_TRACKING = ON
            (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON);
            """);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_databaseCreated) return;

        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync();
        await master.ExecuteAsync($"""
            ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{DatabaseName}];
            """);
    }

    /// <summary>Opens a new connection to the test database.</summary>
    public SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }
}
