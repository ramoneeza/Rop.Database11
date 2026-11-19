# Pruebas de Rop.AbsDatabase11

Pruebas unitarias de la capa abstracta (`AbsDatabase`, `UnitOfWork`, `QueryOne`, `ExecuteScalar`, propagacion del `CancellationToken`). No necesitan SQL Server: usan fakes de `DbConnection`/`DbTransaction`.

## Ejecutar

```powershell
dotnet run --project .\Rop.AbsDatabase11.Tests\Rop.AbsDatabase11.Tests.csproj
```

Con la configuracion actual, `dotnet test` falla con el SDK .NET 11 porque Microsoft Testing Platform requiere una configuracion adicional en `global.json`. Usa el comando anterior o el Explorador de pruebas.
