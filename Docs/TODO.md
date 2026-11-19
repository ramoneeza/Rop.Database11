# TODO

## General / Transversal

- [ ] Crear el nuevo proyecto de tests xUnit (el antiguo `xUnit.Rop.Dapper.ContribEx` se eliminó de la solución)
- [ ] Corregir `CI-CD/get-commits.csc` para listar los ficheros del commit raíz (`git diff-tree` sin `--root` devuelve vacío en el primer commit)
- [ ] Abordar los hallazgos de `Docs/00-fallos-y-mejoras.md` (CE-1 inyección SQL, AB-1 commit con Result fallido, SD-2/SD-3/SD-6 cache, SD-4/SD-5 concurrencia...) según el orden sugerido
- [ ] Aplicar las mejoras C# 15 de `Docs/01-mejoras-net11-csharp15.md` (unions en DeltaChanges/CacheItem, closed en KeyDescription, extension indexers...) según el orden de adopción

## Rop.AbsDatabase11


## Rop.Dapper.ContribEx11


## Rop.SqlDatabase11
