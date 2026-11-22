# Rop.AbsDatabase11 Tests

Unit tests for the abstract layer (`AbsDatabase`, `UnitOfWork`, `QueryOne`, `ExecuteScalar`, `CancellationToken` propagation). They do not require SQL Server: they use `DbConnection`/`DbTransaction` fakes.

## Running

```powershell
dotnet run --project .\Rop.AbsDatabase11.Tests\Rop.AbsDatabase11.Tests.csproj
```

With the current configuration, `dotnet test` fails with the .NET 11 SDK because Microsoft Testing Platform requires additional configuration in `global.json`. Use the command above or the Test Explorer.
