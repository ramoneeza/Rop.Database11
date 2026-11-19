# Pruebas de Change Tracking de Rop.SqlDatabase11

Pruebas del subsistema de Change Tracking: `GetDbVersion`, `GetTableChanges`, `VerifyNotify`, `SqlTableDependency` (evento `OnChanged`), tipos de cambios y handlers DateOnly/TimeOnly.

El fixture crea una base aislada con `CHANGE_TRACKING = ON` a nivel de base de datos; cada test habilita Change Tracking en sus propias tablas. Se necesita SQL Server (por defecto `(localdb)\MSSQLLocalDB`) con Change Tracking disponible (soportado en LocalDB/Express).

## Ejecutar

```powershell
dotnet run --project .\Rop.SqlDatabase11.ChangeTracking.Tests\Rop.SqlDatabase11.ChangeTracking.Tests.csproj
```

o `CI-CD\run-tests-ct.cmd`. Con la configuracion actual, `dotnet test` falla con el SDK .NET 11 porque Microsoft Testing Platform requiere una configuracion adicional en `global.json`.

Variables de entorno: `ROPDB_TEST_SERVER` (instancia con autenticacion Windows) o `ROPDB_TEST_CONNECTION_STRING` (tiene prioridad; el fixture sustituye `Initial Catalog`).
