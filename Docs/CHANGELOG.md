# Changelog

Todos los cambios relevantes de documentación y arquitectura de `Rop.Database11` deben anotarse aquí.

## [2026-11-19]

### Changed

- Historia de git reiniciada: repositorio nuevo con un único commit raíz con el estado actual de la solución (2 librerías — `Rop.AbsDatabase11`, `Rop.SqlDatabase11` — y 3 suites de tests; `Rop.Dapper.ContribEx11` se consume como paquete NuGet 1.0.0).
- `Docs/00-fallos-y-mejoras.md` depurado: eliminadas todas las entradas corregidas/descartadas de la auditoría; quedan solo las aplazadas AB-3 (ExtractConn virtual en el ctor) y AB-8 (política de retry).
- `Docs/01-mejoras-net11-csharp15.md` reescrito como plan de refactorización para esta solución; las propuestas de ContribEx11 se mueven a una sección "fuera de esta solución".
- `Docs/CHANGELOG.md` y `Docs/TODO.md` reiniciados para la nueva etapa.
- Suites de tests verificadas en verde: AbsDb 41, SqlDb 106, ChangeTracking 55 (202 tests, 0 fallos).
