using Dapper;
using Microsoft.Data.SqlClient;

[assembly: AssemblyFixture(typeof(Rop.SqlDatabase11.Tests.TestDatabaseFixture))]

namespace Rop.SqlDatabase11.Tests;

/// <summary>Creates an isolated SQL Server database for one test run.</summary>
public sealed class TestDatabaseFixture : IAsyncLifetime
{
    private bool _databaseCreated;

    public TestDatabaseFixture()
    {
        DatabaseName = $"RopSqlDatabase11Test_{Guid.NewGuid():N}";
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
