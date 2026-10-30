# Cierre de Phase 1 — 2026-10-30

Veredicto: **ACCEPTED para uso local por un operador**, con las limitaciones explícitas al final. Este informe sustituye el veredicto de aceptación de la auditoría anterior para el estado de esta rama; no modifica sus evidencias históricas.

## Funcionalidad y arquitectura

Monolito modular pequeño: `Domain.cs` contiene entidades; `ProductionPolicy.cs`, las reglas deterministas y siguiente acción; `EditorialDb.cs` y las migraciones, la persistencia; `Contracts.cs`, los DTOs y su mapeo; `Program.cs`, HTTP y transacciones. No hay servicios externos, colas, CQRS ni automatización de Google.

Se completaron canal único con zona IANA, calendario de siete días, edición y desactivación de nichos, creación/listado/filtros/detalle/edición de producciones, estados e historial, referencias NotebookLM y siguiente acción manual. Los formularios tienen restricciones, estados de carga, recuperación de errores y controles accesibles. El bloqueo inmediato de envíos y la versión de selección evitan peticiones simultáneas y respuestas de detalle fuera de orden.

El backend devuelve estados textuales, DTOs explícitos y Problem Details para fallos. Se añadió `ModifiedAtUtc`, actualizado al editar tema/fecha, estado o referencia. El snapshot requerido es el nombre del nicho, preservado al renombrarlo o desactivarlo. Los filtros REST aceptan estado y nicho; los de la pantalla operan sobre la lista cargada.

La exclusión mutua entre escrituras usa un bloqueo transaccional PostgreSQL y funciona entre procesos cooperantes. Es la estrategia equivalente segura en base de datos elegida para este alcance de operador único. El cuerpo de éxito se entrega después de confirmar la transacción. La base impone canal único, días únicos y válidos, siete asignaciones al confirmar, integridad de canal/nicho mediante FK compuesta, restricciones de eliminación, estado válido y protección de datos históricos de producción.

## Archivos

- Backend: `Domain.cs`, `EditorialDb.cs`, `Program.cs`, `Validation.cs`, nuevos `Contracts.cs` y `ProductionPolicy.cs`, configuración común y de desarrollo.
- Persistencia: migración `CompleteEditorialConstraints`, diseñador y snapshot EF; se conserva `InitialEditorial`.
- Frontend: `App.tsx` con componentes de configuración y detalle, entrada `main.tsx`, CSS existente ampliado, pruebas de componentes y configuración Vitest/Playwright.
- Pruebas: `ValidationTests.cs`, `ProductionPolicyTests.cs`, `ApiIntegrationTests.cs`, `web/e2e/editorial.spec.ts`.
- Reproducción: solución `Notebook-yt.sln`, manifiesto local `dotnet-ef`, lockfile npm, README y documento de viabilidad.

## Entorno y comandos ejecutados

Checkout reutilizado, limpio al comenzar, rama inicial `audit/phase1-independent`, commit `46fd966`. Se leyeron RTK, README, fuentes, configuración, pruebas y migraciones antes de editar. Se creó `feat/phase-1-editorial-foundation` conservando el historial y la auditoría.

SDK usado: .NET 10.0.403, Node 24.22.1, npm 11.19.1, PostgreSQL 17.6. Docker no estaba disponible. El SDK y PostgreSQL instalados durante la auditoría se reutilizaron desde `%LOCALAPPDATA%/NotebookYt`; se añadió esa ruta del SDK al PATH de cada comando .NET cuando fue necesario.

| Comando u operación | Resultado real |
|---|---|
| `git status --short`, `git branch --show-current`, `git log -1`, `rg --files`, lecturas de fuentes/configuración/pruebas/migraciones | Checkout limpio y estructura revisados |
| `git switch -c feat/phase-1-editorial-foundation` | Rama dedicada creada |
| `Get-Command` de herramientas; `dotnet --list-sdks`, `node --version`, `npm --version`, `postgres --version` | Versiones anteriores confirmadas; sin Docker |
| `impeccable context --target web/src/main.tsx`; lectura de guías de endurecimiento y calidad | Se conservó la interfaz existente; sin PRODUCT.md previo |
| `npm install -D @testing-library/react @testing-library/user-event @testing-library/jest-dom jsdom @playwright/test` | Dependencias de pruebas instaladas y lockfile actualizado |
| `dotnet add tests/NotebookYt.Tests package Microsoft.AspNetCore.Mvc.Testing --version 10.0.7` | TestHost de API instalado |
| `dotnet new sln --format sln`; `dotnet sln add src/NotebookYt.Api tests/NotebookYt.Tests` | Solución creada |
| `dotnet-ef migrations add CompleteEditorialConstraints --project src/NotebookYt.Api` | Migración generada y completada con restricciones PostgreSQL |
| `pg_ctl ... start -o '-h 127.0.0.1 -p 55433'` | Instancia local aislada iniciada |
| `createdb ... phase1_acceptance`; `dotnet-ef database update --project src/NotebookYt.Api` | Dos migraciones aplicadas a una base nueva |
| `dotnet test tests/NotebookYt.Tests` inicial | Detectó valor incorrecto del campo Singleton al insertar; corregido mediante sentinel EF explícito |
| `dotnet test ... --logger 'console;verbosity=detailed'` | Diagnóstico de la restricción Singleton; log local ignorado |
| `npm run build` durante implementación | Detectó opción `exact` no válida en tipos de Testing Library; retirada |
| `npm test` | 7/7 finales: creación/edición, filtros, nichos desactivados, error de guardado sin pérdida del borrador, cancelación, carga y reintento; tres comprobaciones de política UI |
| `npx playwright install chromium`; `npm run test:e2e` inicial | Detectó codificación de etiquetas y contrato vacío del canal no configurado; corregidos |
| `dotnet tool update dotnet-ef --tool-path .audit/tools --version 10.0.7`; `dotnet tool restore` con manifiesto | Herramienta de migraciones fijada a 10.0.7 |
| `dotnet test` mientras el ejecutable estaba abierto en Windows | Falló por bloqueo de archivo; se detuvo el proceso local antes de reconstruir |
| `dotnet test tests/NotebookYt.Tests` final, con `NOTEBOOK_TEST_POSTGRES` aislado | **18/18**, 0 omitidas: 15 unitarias y 3 de integración con PostgreSQL/HTTP |
| `dotnet build src/NotebookYt.Api` final | **0 errores y 0 avisos** |
| `npm run typecheck`; `npm run build` finales | TypeScript y Vite correctos |
| `npm run test:e2e` final contra API y PostgreSQL reales | **2/2**; configuración, edición, estados, referencias, filtros, cancelación, recuperación, móvil y teclado |
| `python tests/api_regression.py` contra API real | **5/5**, incluyendo concurrencia y entradas inválidas |
| `createdb ... phase1_migration_check`; `dotnet-ef database update`; `database update 0`; `database update` | Subida completa, reversión completa y segunda subida correctas en base separada |
| `dotnet ef migrations has-pending-model-changes --project src/NotebookYt.Api` | Sin cambios pendientes de modelo |
| `Invoke-RestMethod /api/productions`, detener API, `pg_ctl ... restart -m fast`, volver a arrancar API, comparar JSON | La producción persistida se conservó idéntica; detalle conservó cinco eventos y notebook |
| `dotnet list src/NotebookYt.Api package --vulnerable --include-transitive`; `npm audit` | Sin vulnerabilidades conocidas en los orígenes consultados |
| `impeccable detect --json src/App.tsx src/style.css` | `[]`, sin incidencias detectadas |
| Navegador preview y captura Playwright | Inspección visual de escritorio y móvil, sin desbordamiento a 375 px; navegación Tab comprobada en E2E |
| `git diff --check`; búsqueda de patrones de secretos en archivos modificados | Sin errores de whitespace ni coincidencias de secretos |
| `Get-NetTCPConnection` para 5080, 5173 y 55433 | Los tres servicios enlazados exclusivamente a 127.0.0.1 |

Los comandos .NET de integración crean una base aleatoria por ejecución, migran y eliminan exclusivamente esa base. Las bases de aceptación y de round-trip se crearon expresamente para esta sesión. No se destruyó ninguna base desconocida ni compartida. El primer SELECT de EF al historial en una base vacía produjo un log de tabla inexistente antes de que la migración crease el historial; el comando completó correctamente.

## Evidencias de los criterios

- Inicialización concurrente de ocho peticiones: un canal, siete asignaciones distintas.
- PostgreSQL rechaza duplicados, día 8, eliminación de una asignación, segundo canal, FK inválida, estado inválido, mutación de snapshot y eliminación de producción con historial; se comprueban los SQLSTATE esperados.
- Creación, recuperación y edición de tema/fecha mediante HTTP y navegador real.
- Desactivar un nicho impide nuevas producciones, conserva las existentes y su snapshot.
- Ocho transiciones repetidas: exactamente una aceptada, siete HTTP 409; historial coherente.
- Ocho registros del notebook: una identidad de referencia. URLs inseguras rechazadas; URLs personales y Enterprise oficiales cubiertas por pruebas.
- Canceladas no admiten edición, transiciones ni registro de referencia.
- Host nuevo de API conserva detalle, historial y notebook; reinicio de proceso API y de PostgreSQL verificado adicionalmente.
- Fechas SQL date y timestamps timestamptz; respuestas UTC y zona IANA verificadas.

## NotebookLM y alcance

La [revisión oficial](NOTEBOOKLM_FEASIBILITY.md) documenta APIs Enterprise de notebooks, fuentes y audio, requisitos de licencia y la diferencia respecto de las funciones de vídeo disponibles en la interfaz. No se verificó API pública soportada de Video Overview o descarga de vídeo ni acceso personal API. No se añadió automatización, publicación, scraping ni acceso a cuentas de Google.

## Riesgos conocidos y límites de aceptación

No quedan bloqueos para los criterios locales de Phase 1. No es una aceptación para desplegar públicamente: no hay autenticación y el servicio debe seguir limitado a localhost. PostgreSQL de esta sesión usa trust en loopback para pruebas efímeras; README exige un usuario y autenticación apropiados para uso habitual.

POST de creación sigue la semántica normal no idempotente: reenviarlo crea otra producción. PUT de referencia sí conserva una sola identidad. No se prometen paginación, multioperador, recuperación tras caída abrupta, pruebas exhaustivas de lector de pantalla/DST ni validación de licencias de Google. Los scripts SQL externos no deben sustituir las transiciones de la API ni su historial.

La aceptación se basa en las pruebas ejecutadas y no implica una garantía de ausencia de defectos. La auditoría previa permanece versionada como antecedente. Los cambios se entregan mediante commits descriptivos en la rama solicitada, sin merge a main ni reescritura de historial.
