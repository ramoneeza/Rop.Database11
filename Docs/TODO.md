# TODO

## Decisiones cerradas

- [x] **AB-8**: política de retry ante fallos transitorios implementada el 2026-11-19 (`RetryPolicy` en AbsDatabase11; writes transaccionales; PayLoad sin retry por decisión)
- [x] **AB-3**: cerrada el 2026-11-19 — decisión del autor: se deja como está (documentada en `Docs/00`); no se aborda

## Refactorización C# 15 (según `Docs/01-mejoras-net11-csharp15.md`)

- [x] U-1: `DeltaChanges` → union `EmptyDelta | TableDelta` aplicada el 2026-11-19 (con `IsEmpty`/`IsNewVersion`/`NoNewVersion`/`Changes`/`GetKeys*` como miembros de la union para minimizar el switch)
- [x] U-2: `CacheItem<T>` → union `CacheState<T>` aplicada el 2026-11-19 (estado interno del cache sin flags; consumidores con pattern matching)
- [x] CA-1/CA-2: DESCARTADAS el 2026-11-19 (sin ganancia real de rendimiento/legibilidad; reconsiderar solo con benchmark en hot path)
- [x] EI-1: extension indexer `repo[key]` sobre `IAbsSimpleSqlRepository<T,K>` aplicado el 2026-11-19; EI-2/EI-3 DESCARTADAS (superficies internas / azúcar trivial)
- [x] CH-1: canal de error de `ICycleTask.PayLoad` tipado con `VoidResult`/`Error` (2026-11-19; decisión: reutilizar Rop.Results9 en lugar de una union propia)
- [x] Labeled break/continue: DESCARTADA por ahora el 2026-11-19 (sin bucles anidados; adoptar si aparece batching)

## CI-CD

- [ ] Corregir `CI-CD/get-commits.csc` para listar los ficheros del commit raíz (`git diff-tree` sin `--root` devuelve vacío en el primer commit)

## Rop.AbsDatabase11

## Rop.SqlDatabase11

- [x] Riqueza de `CacheRepository` completada el 2026-11-19 (simples con DTO int/string; familia CT-free por diseño — la variante CT se eliminó)
- [x] Riqueza de `PartialKeyRepository` completada el 2026-11-19 (CT sin DTO, `ISqlRepository` + `Version`, `Map` identidad en simples sin DTO) + bug latente del ctor base corregido
- [x] `CachePartialKeyRepository` creada el 2026-11-19 (parciales con cache TTL para tablas sin CT; 5 clases) — las 4 familias de repos quedan alineadas
