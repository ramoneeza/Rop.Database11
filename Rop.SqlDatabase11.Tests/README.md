# Rop.SqlDatabase11 Tests

Tests for `CacheRepository` and `MemoryCache` against SQL Server. By default it uses `(localdb)\MSSQLLocalDB` with Windows authentication. You need the .NET 11 SDK, SQL Server LocalDB, and permissions to create and drop databases.

## Running

```powershell
dotnet run --project .\Rop.SqlDatabase11.Tests\Rop.SqlDatabase11.Tests.csproj
```

With the current configuration, `dotnet test` fails with the .NET 11 SDK because Microsoft Testing Platform requires additional configuration in `global.json`. Use the command above or the Test Explorer.

To use a different instance with Windows authentication, set `ROPDB_TEST_SERVER` before running the command:

```powershell
$env:ROPDB_TEST_SERVER = 'localhost\SQLEXPRESS'
```

If you need other connection parameters or SQL authentication, use `ROPDB_TEST_CONNECTION_STRING` instead. It takes precedence over `ROPDB_TEST_SERVER`; the fixture replaces `Initial Catalog` with `master` and with the temporary database name, and disables connection pooling. Do not store passwords in the repository.

## Isolation

The assembly fixture creates a new database with the `RopSqlDatabase11Test_` prefix and a random identifier on every run. When finished, it closes access to that database and drops it; it never drops a pre-existing database with a fixed name. Always use an instance intended for development or testing, never production.
