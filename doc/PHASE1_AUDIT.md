# Auditoría independiente de Phase 1 — 2026-10-30

Veredicto: **REQUIRES CORRECTIONS**. Las correcciones incluidas pasan las pruebas indicadas; la aceptación completa de interfaz y de todos los invariantes no está demostrada.

## Alcance y procedencia

Checkout limpio inicial, rama `main`, HEAD `9bc7610`. Única rama remota disponible: `origin/main`. Phase 1 ya estaba integrada. Se revisaron todos los archivos de aplicación y el diff completo `266e982..9bc7610` (11 archivos, 327 inserciones), tomando `266e982` como estado anterior a Phase 1 según el historial local. No se verificó una rama histórica remota diferente. Rama de entrega: `audit/phase1-independent`. No se hizo push ni merge.

Arquitectura: API mínima ASP.NET Core, EF Core/Npgsql, PostgreSQL y SPA React/Vite. No había migraciones ni pruebas. Las operaciones de NotebookLM son referencias manuales; no se añadió Phase 2.

## Hallazgos significativos

| Severidad | Problema e impacto | Reproducción | Corrección | Regresión |
|---|---|---|---|---|
| Alta | Sin migraciones: una base vacía no soportaba la aplicación | Ejecutar API y consultar canal sobre PostgreSQL vacío | Migración inicial versionada | Aplicación, reaplicación y ciclo subir/bajar/subir sobre base separada |
| Alta | Carreras de inicialización y transiciones: varios canales, historial incoherente o edición después de cancelar | Peticiones simultáneas que leen el mismo estado antes de guardar | Transacción y bloqueo asesor PostgreSQL común a las escrituras; válido entre procesos cooperantes | 8 inicializaciones, 8 transiciones iguales (1 éxito, 7 conflictos), 8 registros de referencia (una sola fila) |
| Alta | El registro de notebook permitía mutar producciones canceladas | Cancelar y PUT `/api/productions/{id}/notebook` | Comprobar estado dentro de la transacción | HTTP 409; también edición y transición rechazadas |
| Alta | API y frontend no compartían contrato de enum: UI enviaba texto y API esperaba números | POST status con `BriefReady` | JSON enum textual, rechazo de números, desconocidos, nulos y ausencia | Integración de estados y creación; smoke de navegador |
| Alta | Dependencias de desarrollo con avisos de seguridad | `npm audit`; restauración NuGet original | Vitest 5.0.5, EF 10.0.7 y proveedor Npgsql 10.0.2; lockfile npm | Auditorías finales sin avisos conocidos |
| Media | Nulos en descripción/guía causaban excepción; zona IANA dependía de identificadores del SO | PUT niche con description null; zona Windows en Windows | Validación explícita de nulos e IANA | 11 casos unitarios y pruebas HTTP |
| Media | URL aceptaba credenciales, puertos y rutas que solo contenían notebook | PUT con usuario en URL, puerto 999 o ruta sin ID | HTTPS, host exacto, puerto normal, sin usuario y ruta de notebook con identificador | Unitarias e integración de URLs válidas e inválidas |
| Media | Nichos carecían de FK al canal | Escritura SQL con ChannelId inexistente | FK restrictiva en modelo y migración | PostgreSQL rechaza la escritura |
| Media | La UI ofrecía transiciones ilegales y registro tras cancelación | Abrir Draft o Cancelled | Acciones según reglas del backend y botón de referencia bloqueado | Tres pruebas frontend y reglas verificadas por HTTP |
| Baja | Mensajes HTTP vacíos, selección de nicho deshabilitado retenida y sin reintento de carga | API caída o deshabilitar nicho seleccionado | Mensaje con código HTTP, reintento, selección válida, límites y señal de guardado | Recuperación observada en navegador; build TypeScript |

No se identificó una vulnerabilidad crítica explotable de la aplicación. Los avisos críticos iniciales de npm afectaban herramientas de desarrollo; su severidad de catálogo no demuestra explotación en este despliegue local.

## Evidencia y límites

- PostgreSQL 17.6 aislado en perfil local; instancia verificada en `127.0.0.1:55433`, bases `editorial` y `migration_roundtrip`. Autenticación trust exclusivamente para este entorno efímero local, nunca recomendada para despliegue. No se versionaron credenciales.
- API en `127.0.0.1:5080`, Vite en `127.0.0.1:5173`. Sin publicación. Sin autenticación: uso exclusivo por un operador local. Se rechazaron orígenes de navegador ajenos a los dos orígenes locales de Vite; no se habilitó CORS abierto. No se registran cuerpos ni se habilita logging de datos sensibles.
- Se verificaron exactamente siete asignaciones por API tras inicialización concurrente; unicidad y rango de weekday, FK de canal y NOT NULL se probaron directamente en SQL.
- El bloqueo protege escrituras de esta API. No protege scripts SQL externos que omitan el protocolo. La base no impone por sí sola exactamente siete filas, un único canal, un enum válido, inmutabilidad de snapshot ni correspondencia compuesta entre canal y nicho de una producción. Debe decidirse y verificarse ese límite antes de aceptación estricta de invariantes de persistencia.
- Snapshot actual: solo nombre del nicho; se comprobó que renombrarlo no altera producciones existentes. Descripción y guía no se capturan. No hay contrato externo disponible que permita afirmar que el snapshot editorial está completo.
- PUT de notebook duplicado y transición duplicada están probados. POST de creación de producción no tiene clave de idempotencia: un reintento crea otra producción. No se impuso unicidad especulativa por tema/fecha porque impediría producciones legítimas.
- Fecha planificada usa DateOnly. CreatedAtUtc usa UTC y sale con Z; columnas temporales de PostgreSQL son timestamptz. No se ejecutó una matriz exhaustiva de cambios DST.
- Reinicio real de API sobre la misma base; persistieron producciones e historial y volvió a pasar la integración. No se reinició PostgreSQL para verificar recuperación después de fallo del servidor.
- Navegador real: fallo inicial sin backend, recuperación mediante reintento, siete días cargados, creación de producción, detalle e historial. A 375 px, innerWidth=375 y scrollWidth=360. No hay prueba visual completa: `preview_snapshot` agotó 15000 ms en dos intentos. No se completó la revisión de teclado, todos los botones, todas las transiciones y errores de red durante operaciones. El smoke fue interactivo; no es una suite E2E reproducible versionada.

## Registro de comandos y resultados

Comandos ejecutados desde la raíz salvo los npm, ejecutados en web. Se agrupan lecturas repetidas; herramientas y servidores quedaron fuera del repositorio o en `.audit/` ignorado. Los comandos de setup no contienen secretos.

| Comando / operación | Resultado |
|---|---|
| `Get-Content C:\Users\mozot\.codex\RTK.md`; `Get-Location`; `git status --short`; `git branch -a`; `git log --oneline --all` y `--reverse`; `git remote -v` | Checkout e historial inspeccionados |
| `rg --files`; lecturas `Get-Content` de README, cs/csproj, TSX, CSS y configuración; `git diff 266e982 HEAD --stat`; `git diff 266e982 HEAD` | Revisión completa del código y diff; copia local ignorada en .audit |
| `Get-Command dotnet,node,npm,docker,psql,winget,gh`; `dotnet --list-sdks`; inspección de directorios PostgreSQL | SDK 5/9 disponibles; faltaban SDK 10, Docker y PostgreSQL |
| `git switch -c audit/phase1-independent` | Rama dedicada creada |
| Descargar y ejecutar `dotnet-install.ps1 -Channel 10.0 -InstallDir <LocalAppData>/NotebookYt/dotnet -NoPath` | SDK 10.0.403 instalado |
| `npm install`; `npm audit --json` | Inicialmente 1 aviso moderado y 2 críticos |
| `npm install -D vitest@5.0.5`; `npm audit` | 0 vulnerabilidades finales |
| `dotnet build src/NotebookYt.Api` inicial | Éxito, avisos de paquetes y API obsoleta; corregidos |
| `dotnet tool install dotnet-ef --tool-path .audit/tools --version 10.0.0` | Instalado; aviso posterior por versión menor que runtime |
| `dotnet add src/NotebookYt.Api package ...` para EF Design/Core/Relational 10.0.7 y Npgsql EF 10.0.2 | Dependencias actualizadas y alineadas |
| `dotnet-ef migrations add InitialEditorial --project src/NotebookYt.Api` | Migración generada |
| Descarga PostgreSQL por Invoke-WebRequest/Expand-Archive; segunda descarga con curl; extracción selectiva bin/lib/share con Python | La primera ruta fue lenta; ruta mínima usada para verificar |
| `initdb ...pg-data-verified` antes de terminar extracción | Falló por postgres.bki ausente; no arrancó servidor |
| `initdb -D ...pg-data-verified -U postgres -A trust --encoding=UTF8 --locale=C`; `pg_ctl ... -o '-h 127.0.0.1 -p 55433' start`; `createdb ... editorial` | Éxito tras extracción completa |
| `dotnet-ef database update --project src/NotebookYt.Api` con conexión local de auditoría | Migración aplicada; el primer SELECT a tabla de historial inexistente fue seguido de creación correcta |
| `dotnet run --project src/NotebookYt.Api` / `--no-build`; `npm run dev` | Servidores locales iniciados |
| `python tests/api_regression.py` antes de inicio completo | Connection refused; 0 pruebas ejecutadas |
| `python tests/api_regression.py` tras inicio y después de reinicios | 5/5 pasan; múltiples solicitudes por caso |
| `dotnet new xunit -o tests/NotebookYt.Tests --framework net10.0`; `dotnet add tests/NotebookYt.Tests reference src/NotebookYt.Api` | Proyecto unitario creado |
| `dotnet test tests/NotebookYt.Tests` | 11/11 pasan; conflictos iniciales EF corregidos fijando Core/Relational |
| `python tests/database_regression.py` | 4 restricciones SQL verificadas; cada operación inválida en transacción revertida |
| `dotnet-ef migrations has-pending-model-changes --project src/NotebookYt.Api` | Sin cambios pendientes |
| `dotnet-ef database update` repetido | Base ya actualizada |
| `psql ... -c '\d "Niches"'` desde PowerShell | Falló el quoting del identificador; sustituido por regresiones SQL ejecutables |
| `createdb ... migration_roundtrip`; `dotnet-ef database update`; `database update 0`; `database update` | Ciclo completo correcto en base separada |
| `Get-NetTCPConnection -LocalPort 5080`; `Stop-Process -Id <PID API>`; nuevo `dotnet run` | Reinicio controlado de API, datos conservados |
| `dotnet build src/NotebookYt.Api` final | 0 errores, 0 avisos |
| `npm run build` | TypeScript y Vite correctos |
| `npm test` | 3/3 pasan |
| `dotnet list src/NotebookYt.Api package --vulnerable --include-transitive` | Sin paquetes vulnerables conocidos en fuentes actuales |
| `git diff --check` | Sin errores de whitespace |
| Herramientas preview status/open/navigate/evaluate/act/resize | Recorrido navegador parcial; captura agotó timeout, un selector de click fue rechazado y se sustituyó por selección DOM observada |

Pendiente para aceptación: resolver el contrato de idempotencia y alcance de snapshots/invariantes SQL, completar suite E2E reproducible y comprobación visual/teclado, y verificar reinicio del servidor PostgreSQL. Ninguna de estas comprobaciones se declara aprobada por compilar.

Cierre: pesta?a de auditor?a cerrada, procesos de descarga/extracci?n redundantes detenidos y servidores locales detenidos; bases conservadas fuera del repositorio. `git add` y `git commit -m "fix: harden Phase 1 persistence and API contracts with audit regressions"` ejecutados para entrega local.
