# Changelog

Todos los cambios relevantes de documentación y arquitectura de `Rop.Database11` deben anotarse aquí.

## [2026-11-19]

### Changed

- Renombrados el directorio de la solución y el fichero de solución (`Rop.Database.11.slnx` → `Rop.Database11.slnx`). Actualizadas las URLs de GitHub (`RepositoryUrl`/`PackageProjectUrl`) y las referencias en READMEs y Docs al nuevo nombre `Rop.Database11`.
- Extraído `Rop.Dapper.ContribEx11` (con su proyecto de tests) a su propia solución/repositorio. Esta solución ahora lo consume como paquete NuGet (`Rop.Dapper.ContribEx11` 1.0.0 desde el feed `nugeteeza`): sustituidos los `ProjectReference` de `Rop.AbsDatabase11`, `Rop.SqlDatabase11.Tests` y `Rop.SqlDatabase11.ChangeTracking.Tests` por `PackageReference`. Eliminados de CI-CD los scripts `run-tests-dapper.cmd`, `docs-dapper.cmd` y `coverage-dapper.xml`; `run-tests.cmd` ahora ejecuta las 3 suites restantes y `coverage.csc` pierde la clave `dapper`. README actualizado (ContribEx11 como dependencia externa).

## [2026-11-15]

### Added

- Creado `Rop.Database.11` a partir de la copia de `Rop.Database.10`: renombrados a `.11` las carpetas, proyectos, ficheros, namespaces, PackageId, URLs del repositorio y READMEs (`Rop.AbsDatabase11`, `Rop.Dapper.ContribEx11` y `Rop.SqlDatabase11`). (5dd2d89)
- Migrado a .NET 11: `TargetFramework` a `net11.0` (C# 15) en los tres proyectos, `System.Runtime.Caching` a 11.0.0 y `Microsoft.Data.SqlClient` a 7.1.2 (desaparecen los avisos NU1902/NU1903 de `System.Security.Cryptography.Pkcs` transitivo). (5dd2d89)
- Convertida la solución a formato `.slnx` (`Rop.Database.11.slnx`) con `dotnet sln migrate`. (5dd2d89)
- Añadida referencia local de proyecto de `Rop.AbsDatabase11` a `Rop.Dapper.ContribEx11` (usa `KeyDescription`/`PartialKeyDescription` de ese proyecto). (5dd2d89)
- Añadidos `Docs/CHANGELOG.md` y `Docs/TODO.md` siguiendo la convención de otros proyectos; incluidos en la solución junto a los scripts de `CI-CD/`. (pendiente de commit)
- Añadido `Docs/00-fallos-y-mejoras.md`: auditoría previa a la refactorización con 38 hallazgos numerados (AB-1.., CE-1.., SD-1.., GE-1..) con severidad, ubicación y sugerencia. (pendiente de commit)
- Añadido `Docs/01-mejoras-net11-csharp15.md`: análisis de aplicación de Union Types, Closed Hierarchies, Collection Expression Arguments, Extension Indexers y Labeled break/continue al refactor de la librería, con orden de adopción y riesgos. (pendiente de commit)

### Changed

- `CI-CD/get-commits.csc`: la detección de la raíz de la solución pasa de buscar el fichero hardcodeado `Intranet2026.slnx` a auto-detectar cualquier `*.slnx` subiendo desde el directorio actual. (pendiente de commit)
- Documentación: actualizados `Docs/00-fallos-y-mejoras.md` y `Docs/01-mejoras-net11-csharp15.md` para reflejar que la librería se centra exclusivamente en SQL Server (eliminados los avisos multi-proveedor). (pendiente de commit)
- Documentación: reformulado el hallazgo CE-4 de `Docs/00-fallos-y-mejoras.md`: la reflexión a internals de Dapper.Contrib se mantiene (es necesaria para reutilizar sus caches y garantizar consistencia con Dapper.Contrib); el plan pasa a mitigar el riesgo con test de arranque, mensajes claros y versión fijada. (pendiente de commit)

### Removed

- Eliminado de la solución el proyecto de tests `xUnit.Rop.Dapper.ContribEx` (se creará un proyecto de tests nuevo más adelante). (5dd2d89)
- Eliminado `Rop.Database.11.sln` tras la migración a `.slnx`. (5dd2d89)
