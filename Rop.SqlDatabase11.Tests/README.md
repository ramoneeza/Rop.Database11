# Pruebas de Rop.SqlDatabase11

Pruebas de `CacheRepository` y `MemoryCache` contra SQL Server. Por defecto utiliza `(localdb)\MSSQLLocalDB` con la identidad de Windows. Se necesita el SDK de .NET 11, SQL Server LocalDB y permisos para crear y eliminar bases de datos.

## Ejecutar

```powershell
dotnet run --project .\Rop.SqlDatabase11.Tests\Rop.SqlDatabase11.Tests.csproj
```

Con la configuracion actual, `dotnet test` falla con el SDK .NET 11 porque Microsoft Testing Platform requiere una configuracion adicional en `global.json`. Usa el comando anterior o el Explorador de pruebas.

Para otra instancia con autenticacion Windows, establece `ROPDB_TEST_SERVER` antes de ejecutar el comando:

```powershell
$env:ROPDB_TEST_SERVER = 'localhost\SQLEXPRESS'
```

Si necesitas otros parametros de conexion o autenticacion SQL, usa `ROPDB_TEST_CONNECTION_STRING` en su lugar. Tiene prioridad sobre `ROPDB_TEST_SERVER`; el fixture sustituye `Initial Catalog` por `master` y por el nombre de la base temporal, y desactiva el pool de conexiones. No guardes contrasenas en el repositorio.

## Aislamiento

El fixture de ensamblado crea una base nueva con el prefijo `RopSqlDatabase11Test_` y un identificador aleatorio en cada ejecucion. Al finalizar cierra el acceso a esa base y la elimina; nunca elimina una base preexistente con nombre fijo. Usa siempre una instancia destinada a desarrollo o pruebas, no produccion.
