# Rop.SqlDatabase11 Change Tracking Tests

Tests for the Change Tracking subsystem: `GetDbVersion`, `GetTableChanges`, `VerifyNotify`, `SqlTableDependency` (`OnChanged` event), change types, and the DateOnly/TimeOnly handlers.

The fixture creates an isolated database with `CHANGE_TRACKING = ON` at the database level; each test enables Change Tracking on its own tables. SQL Server is required (default `(localdb)\MSSQLLocalDB`) with Change Tracking available (supported on LocalDB/Express).

## Running

```powershell
dotnet run --project .\Rop.SqlDatabase11.ChangeTracking.Tests\Rop.SqlDatabase11.ChangeTracking.Tests.csproj
```

or `CI-CD\run-tests-ct.cmd`. With the current configuration, `dotnet test` fails with the .NET 11 SDK because Microsoft Testing Platform requires additional configuration in `global.json`.

Environment variables: `ROPDB_TEST_SERVER` (instance with Windows authentication) or `ROPDB_TEST_CONNECTION_STRING` (takes precedence; the fixture replaces `Initial Catalog`).
