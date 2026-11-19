# TODO

## Decisiones cerradas

- [x] **AB-8**: política de retry ante fallos transitorios implementada el 2026-11-19 (`RetryPolicy` en AbsDatabase11; writes transaccionales; PayLoad sin retry por decisión)
- [x] **AB-3**: cerrada el 2026-11-19 — decisión del autor: se deja como está (documentada en `Docs/00`); no se aborda

## Refactorización C# 15 (según `Docs/01-mejoras-net11-csharp15.md`)

- [x] U-1: `DeltaChanges` → union `EmptyDelta | TableDelta` aplicada el 2026-11-19 (con `IsEmpty`/`IsNewVersion`/`NoNewVersion`/`Changes`/`GetKeys*` como miembros de la union para minimizar el switch)
- [x] U-2: `CacheItem<T>` → union `CacheState<T>` aplicada el 2026-11-19 (estado interno del cache sin flags; consumidores con pattern matching)
- [ ] CA-1/CA-2: collection expression arguments en `MemoryCache` y listas con capacidad
- [ ] EI-1/EI-2/EI-3: extension indexers en `IAbsSimpleSqlRepository`, `MemoryCache`, `RepositoryDictionary`, `Database`
- [ ] CH-1: tipar el canal de error de `ICycleTask.PayLoad` (`string?` → jerarquía cerrada) si procede
- [ ] Labeled break/continue cuando aparezcan bucles anidados (batching, procesamiento de deltas)

## CI-CD

- [ ] Corregir `CI-CD/get-commits.csc` para listar los ficheros del commit raíz (`git diff-tree` sin `--root` devuelve vacío en el primer commit)

## Rop.AbsDatabase11

## Rop.SqlDatabase11

- [x] Riqueza de `CacheRepository` completada el 2026-11-19 (simples con DTO int/string; familia CT-free por diseño — la variante CT se eliminó)
