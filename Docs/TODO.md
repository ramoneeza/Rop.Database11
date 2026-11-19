# TODO

## Decisiones pendientes

- [ ] **AB-3**: decidir sobre `ExtractConn` virtual llamada desde el constructor base (`Rop.AbsDatabase11/AbsDatabase.cs:53, 62`) — verificar uso real en entorno antes de hacerla no virtual o perezosa
- [ ] **AB-8**: decidir la política de retry ante fallos transitorios (propuesta: errores 1205/-2, 3 intentos, backoff 100→250ms, configurable por instancia, Result fallido al agotar)

## Refactorización C# 15 (según `Docs/01-mejoras-net11-csharp15.md`)

- [ ] U-1: `DeltaChanges` → union `EmptyDelta | TableDelta` (breaking público)
- [ ] U-2: `CacheItem<T>` → union `CacheState<T>` (breaking público)
- [ ] CA-1/CA-2: collection expression arguments en `MemoryCache` y listas con capacidad
- [ ] EI-1/EI-2/EI-3: extension indexers en `IAbsSimpleSqlRepository`, `MemoryCache`, `RepositoryDictionary`, `Database`
- [ ] CH-1: tipar el canal de error de `ICycleTask.PayLoad` (`string?` → jerarquía cerrada) si procede
- [ ] Labeled break/continue cuando aparezcan bucles anidados (batching, procesamiento de deltas)

## CI-CD

- [ ] Corregir `CI-CD/get-commits.csc` para listar los ficheros del commit raíz (`git diff-tree` sin `--root` devuelve vacío en el primer commit)

## Rop.AbsDatabase11

## Rop.SqlDatabase11
