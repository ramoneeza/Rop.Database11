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
- U-2 aplicada: `CacheItem<T>` eliminado y sustituido por la **union** `CacheState<T>(ValidItem<T>, JustExpiredItem, ExpiredItem)` (`CacheRepository/CacheState.cs`). `MemoryCache<K,T>` guarda estados de union y `TryGetValue` devuelve `CacheState<T>?` (null = clave inexistente) calculando el estado con un único switch sobre `ExpireAt`; desaparece el `InternalCacheItem`. Los consumidores del repo cache usan pattern matching. Breaking: `CacheItem<T>` y el `TryGetValue` con `out` desaparecen. Tests nuevos de transición de estados. Suites en verde (217 tests).

### Added

- Riqueza de `CacheRepository` completada: `AbsSimpleCacheSqlIntRepository<T,D>` y `AbsSimpleCacheSqlRepository<T,D>` (simples con DTO, claves int/string). Tests: 2 nuevos en `CacheRepositoryTests` (interfaces y claves DTO int/string).

- Nueva carpeta `CachePartialKeyRepository` (repositorios parciales con cache para tablas SIN change tracking): `AbsSimpleCacheSqlPartialRepositoryK<T,D,K>` (agrupa por primera clave parcial en `PartialBlock` y cachea con TTL) + 4 especializaciones (`AbsSimpleCacheSqlPartialIntRepository<T,D>`, `AbsSimpleCacheSqlPartialRepository<T,D>`, `AbsSimpleCacheSqlPartialRepository<T>`, `AbsSimpleCacheSqlPartialIntRepository<T>`). La base cache simple (`AbsSimpleCacheSqlRepositoryK`) gana un ctor con `KeyDescription` explícito, `FindKeyPropT` virtual y `KeyDescription` virtual (mismo arreglo del bug latente que en la base normal). Tests: 2 nuevos en `CachePartialRepositoryTests` (agrupación + recarga al expirar). Suites en verde (223 tests).

### Removed

- Eliminada la variante con change tracking de `CacheRepository` (`AbsCacheSqlRepositoryK<T,D,K>`, `AbsCacheSqlDtoRepository<T,D>`, `AbsCacheSqlDtoIntRepository<T,D>`, `AbsCacheSqlRepository<T,K>`, `AbsCacheSqlRepository<T>`, `AbsCacheSqlIntRepository<T>`) por decisión de diseño: el cache está pensado para tablas/bases de datos SIN control de cambios (la expiración sustituye al CT); el híbrido cache+CT era conceptualmente redundante con los repositorios normales con CT. `CacheRepository` queda como familia CT-free sobre `IAbsSimpleSqlRepository<T,K>` (ya sin CT, sin cambios de interfaz). Suites en verde (219 tests).
- Riqueza de `PartialKeyRepository` completada para igualar a `Repository`: `AbsSqlPartialRepository<T>` y `AbsSqlPartialIntRepository<T>` (change tracking sin DTO, claves string/int, con `GetKey` virtual y `Map` identidad); `AbsSqlPartialRepositoryK<T,D,K>` implementa ahora `ISqlRepository<PartialBlock<K,T>,K>` (añadida `Version`); los simples sin DTO añaden `Map` identidad.
- Bug latente corregido: el constructor de `AbsSimpleSqlRepositoryK` resolvía la BD externa con `FactoryExternalDatabase<D>()` y la propiedad clave con el nombre del DTO, lo que rompía los repos parciales (`D`/`T` son `PartialBlock`, sin atributos de tabla). Ahora usa el `KeyDescription` recibido y una resolución virtual `FindKeyPropT` que el repo parcial especializa a `PartialBlock<K,T>.Key`. Además se eliminó del core parcial el abstract `GetKey(T)` (colisionaba con `GetKey(D)` cuando T==D; `GetKey` ahora vive solo en las variantes sin DTO, como en `Repository`). Tests: 2 nuevos en `PartialRepositoryTests`. Suites en verde (221 tests).
- Tests: 14 nuevos en `RetryPolicyTests` (Rop.AbsDatabase11.Tests). Suites en verde: AbsDb 55, SqlDb 106, CT 55 (216 tests).
