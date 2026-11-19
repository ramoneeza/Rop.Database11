# Changelog

Todos los cambios relevantes de documentación y arquitectura de `Rop.Database11` deben anotarse aquí.

## [2026-11-19]

### Changed

- Historia de git reiniciada: repositorio nuevo con un único commit raíz con el estado actual de la solución (2 librerías — `Rop.AbsDatabase11`, `Rop.SqlDatabase11` — y 3 suites de tests; `Rop.Dapper.ContribEx11` se consume como paquete NuGet 1.0.0).
- `Docs/00-fallos-y-mejoras.md` depurado: eliminadas todas las entradas corregidas/descartadas de la auditoría; quedan solo las aplazadas AB-3 (ExtractConn virtual en el ctor) y AB-8 (política de retry).
- `Docs/01-mejoras-net11-csharp15.md` reescrito como plan de refactorización para esta solución; las propuestas de ContribEx11 se mueven a una sección "fuera de esta solución".
- `Docs/CHANGELOG.md` y `Docs/TODO.md` reiniciados para la nueva etapa.
- Suites de tests verificadas en verde: AbsDb 41, SqlDb 106, ChangeTracking 55 (202 tests, 0 fallos).

### Added

- Política de reintentos ante errores transitorios (AB-8): clase `RetryPolicy` en `Rop.AbsDatabase11` (3 reintentos con backoff 100/250/500 ms por defecto, `NoRetry`, predicado configurable, detector estándar de SqlException sin dependencia de SqlClient) expuesta como `AbsDatabase.RetryPolicy` y aplicada en los 4 cores de UnitOfWork (sync/async × con/sin transacción).

### Changed

- Todos los writes internos de `AbsDatabase` pasan a la ruta transaccional (Insert/Update/InsertOrUpdate/Execute/Delete/DeleteByKey/UpdateIdValue/DeleteByPartialKey/InsertNoKeyAtt/InsertOrUpdateMerge, sync y async) para que el reintento sea seguro; la ruta sin transacción queda para lecturas.
- `SqlTableDependency.PayLoad` no reintenta (decisión): el polling se autocura en el siguiente ciclo.
- AB-3 cerrada por decisión del autor: `ExtractConn` se mantiene `virtual` y llamada desde el constructor base, documentada como advertencia para consumidores externos.

### Changed (refactor C# 15)

- U-1 aplicada: `DeltaChanges` pasa de record con flags a **union** `DeltaChanges(EmptyDelta, TableDelta)` (`Tracking/DeltaChanges.cs`). `EmptyDelta` lleva `OldVersion` para conservar la semántica de `IsNewVersion`/`NoNewVersion`; las propiedades de estado, `Changes`, `GetKeys*` y `GetPartialKeys*` se mantienen como miembros de la union (switch interno) para minimizar el impacto en consumidores — `PayLoad` y los handlers de los repos no cambiaron. `ConnectionHelper.GetTableChanges` crea `EmptyDelta` con versión nueva cuando todos los cambios se filtran como `Unknown`. Breaking: el constructor público `new DeltaChanges(...)` desaparece (`new TableDelta(...)` / `new EmptyDelta(...)`). Suites en verde (216 tests).
- Tests: 14 nuevos en `RetryPolicyTests` (Rop.AbsDatabase11.Tests). Suites en verde: AbsDb 55, SqlDb 106, CT 55 (216 tests).
