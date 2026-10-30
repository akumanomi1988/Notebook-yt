# Aceptación operativa independiente — 2026-10-30

**FUNCTIONALLY COMPLETE WITH EXTERNAL VERIFICATION PENDING.** El recorrido local disponible está probado con autenticación activa, PostgreSQL real, medios técnicos, reinicios y restauración aislada. No se acepta todavía el recorrido completo con proveedores: faltan una exportación genuina de NotebookLM, OAuth de Google utilizable, comprobación de licencia Enterprise y Docker ejecutable. No hubo subida real ni publicación pública.

## Base y alcance

Repositorio local `C:\Users\mozot\Notebook-yt`, árbol inicialmente limpio. HEAD local y remoto de `feat/complete-notebook-yt`: `485c0d84ff345542736b14639b86a249ae5340e8`. Rama de trabajo: `audit/complete-product-acceptance`; se conserva la rama original. Se leyeron las instrucciones RTK y los tres documentos requeridos antes de modificar código. Se revisaron API, autenticación, OAuth, persistencia, trabajador, almacenamiento, contratos Google, frontend, Docker y pruebas. No se rediseñó el producto ni se añadieron generadores alternativos.

Entorno ejecutado: Windows, .NET SDK 10.0.403, Node 24.22.1, npm 11.19.1, PostgreSQL 17.6, FFmpeg/FFprobe 9.0.2, Chromium de Playwright. Servicios limitados a `127.0.0.1`, API en 5080 y PostgreSQL de pruebas en 55433. Las bases tienen nombres aleatorios; no se sobrescribió ninguna base del operador.

## Hallazgos y correcciones

| Severidad | Problema e impacto | Reproducción | Corrección y regresión |
|---|---|---|---|
| High | El filtro Origin solo admitía Vite. El frontend empaquetado no podía iniciar sesión ni guardar desde su propio puerto. | La regresión con la política original devuelve 403 en el login con CSRF válido; debía devolver 200. Se ejecutó y falló por ese motivo. | Se admite el origen de la petición actual, manteniendo AllowedHosts, autenticación y CSRF. `OperatorSecurityTests` comprueba login del mismo origen y rechazo de origen ajeno; E2E pasa en 5080. |
| High | Una fecha futura activaba publicación pública aunque la visibilidad elegida fuera `private` o `unlisted`. Un turno del calendario podía introducir esa fecha manteniendo el valor privado predeterminado. | La prueba controlada del publicador con snapshot privado y fecha futura enviaba `publishAt` a YouTube sin rechazar la contradicción. Se ejecutó sin la protección y falló por ausencia de la excepción esperada. | Política central: programar requiere seleccionar `public` expresamente. Validación en edición, calidad, revisión y trabajos persistidos de subida/publicación, antes de llamar al proveedor. Se prueban las seis combinaciones, rechazo HTTP 409 sin alterar la fecha guardada, cero llamadas adicionales del adaptador y recuperación por UI al borrar la fecha. |
| Medium | La aceptación E2E anterior desactivaba autenticación. No demostraba el acceso privado del recorrido operativo. | Los archivos anteriores y README exigían `Security__RequireAuthentication=false`. | Fixture autenticado con login real por UI, sesión reutilizada y CSRF en peticiones auxiliares. Cuatro E2E pasan sin modificar las protecciones ni el límite de login. |
| Medium | No existía evidencia de restauración; conservar filas tras reiniciar no demuestra recuperación de backup. | Documentación anterior declaraba restauración no ejecutada y no había scripts. | Scripts Windows con dump custom, medios, claves, referencias de configuración, hashes y ACL. Prueba de todas las tablas, claves DPAPI, archivos, permisos, corrupción y rechazo de sobrescritura. Aplicación restaurada arrancada y consultada. |
| Low | El cliente suponía JSON incluso ante errores HTML/texto. Mostraba una excepción de parseo y perdía el estado HTTP útil para recuperar el flujo. | Respuesta 502 HTML o 413 texto a la importación. | Lectura común tolerante a errores no JSON; no muestra el cuerpo diagnóstico. Cuatro pruebas frontend cubren reintento, subida, respuesta 200 inválida y validación Problem Details. |

No se identificaron vulnerabilidades Critical reproducibles en la revisión realizada. Esto es una revisión de las superficies indicadas y pruebas concretas, no una certificación de ausencia de vulnerabilidades.

## Resultados ejecutados

| Comprobación | Resultado actual |
|---|---|
| Backend build | Correcto, 0 advertencias y 0 errores |
| xUnit completo | **52 correctas, 0 fallidas, 0 omitidas**, 26 s |
| TypeScript | Correcto |
| Frontend build | Correcto, Vite 7.3.7 |
| Componentes/cliente | **14 correctas** en 5 archivos |
| Playwright, aplicación empaquetada | **4 correctas**, 10,6 s; login UI, cookies y CSRF activos |
| NuGet, incluidas transitivas | Ningún paquete vulnerable informado por las fuentes consultadas |
| npm audit | 0 vulnerabilidades informadas |
| Migraciones | Las tres se aplican en bases nuevas; rollback a 0 y reaplicación correctos; modelo sin cambios pendientes |
| Backup/restore | Dump y restauración reales en PostgreSQL, con medios, claves y permisos Windows |
| Reinicios | API y PostgreSQL reiniciados; sesión, datos, historia y medios conservados |
| Docker | No hay CLI, Compose, ejecutable en ubicación habitual ni servicio Docker disponible. Solo revisión estática |

Las pruebas PostgreSQL ejercitan siete nichos, restricciones, referencias, snapshots, cancelación, cambios concurrentes y duplicados. Las pruebas ampliadas cubren paquetes, tareas, calendario/DST, calidad, miniatura con recorte/texto, fallo multimedia, recuperación y contratos oficiales controlados. Se añadieron errores Google controlados 401/403/429 para comprobar mensajes saneados sin exponer el cuerpo del proveedor. Ninguna de estas respuestas acredita una cuenta Google real.

La fecha de YouTube no es una fecha editorial inocua: el contrato oficial de [`status.publishAt`](https://developers.google.com/youtube/data/reference/rest/v3/videos#status.publishAt) programa publicación; exige `privacyStatus=private` durante la espera. Por eso la aplicación exige la intención **public** del operador, aunque el payload oficial use **private** hasta llegar esa fecha. Para conservar el vídeo privado o no listado hay que dejar vacía la programación pública. Los registros previos contradictorios se conservan y el trabajador se detiene para revisión, sin aplicar una programación pública.

Playwright cubre canal, siete asignaciones, creación/edición/filtros de producción, estados e historia, referencias válidas e inválidas, nichos desactivados, fallo 503 y recuperación, temas aprobados, investigación, paquete, primera tarea manual, bloqueo de publicación sin vídeo válido, importación e inspección por trabajador, reproducción del archivo y bloqueo del canónico sin procedencia. También comprueba anchura móvil y navegación de teclado. No se presenta esta cobertura como una ejecución de todos los pasos con un vídeo genuino: ese recorrido sigue pendiente.

## Persistencia y restauración

La prueba `BackupRestoreTests` construye el recorrido técnico controlado con producción, siete nichos, fuentes, paquete, ocho tareas, vídeo, miniatura, revisión, eventos e historia. Guarda un valor cifrado de prueba sin credenciales reales. Ejecuta los scripts entregados, restaura en `notebook_restore_<guid>`, compara el contenido de **todas las tablas del modelo**, hashes de archivos y descriptores ACL, y descifra el valor usando las claves restauradas y el mismo usuario Windows. Rechaza un destino existente y un dump modificado. Borra únicamente sus bases/directorios efímeros identificados.

Además, `Test-LocalPersistence.py` se ejecutó contra la instancia HTTP real creada por `Start-LocalAcceptance.ps1`. Paró API/trabajador, ejecutó backup, reinició PostgreSQL y API, comparó las respuestas autenticadas y checksums, restauró en otra base/directorios, arrancó la copia y volvió a comparar. La cookie de sesión anterior siguió siendo válida: las claves persistieron. Se volvió a seleccionar la base original de pruebas.

Último ensayo: origen `notebook_acceptance_055d1053ac804b77aa682341388585b4`, restauración `notebook_restore_f16f828d289160aa5a0aeb88275a9370`. Conservaba 6 producciones, 2 fuentes, 16 tareas y 2 artefactos técnicos `Valid/Unverified`; ambos trabajos `InspectArtifact` estaban `Succeeded`. El mismo archivo puede estar referenciado por varias producciones sin duplicar su blob. No había vídeo genuino ni subida real.

Se verificó recuperación **local en Windows**, no en otra máquina, contenedor o Linux. DPAPI exige las condiciones del usuario/máquina original. Las referencias de configuración no contienen valores: el operador debe conservar por separado su configuración privada. El manifiesto SHA256 detecta corrupción, no prueba autenticidad frente a manipulación de la propia copia. Procedimiento exacto: [OPERATIONS.md](OPERATIONS.md).

## NotebookLM y cuentas externas

No se aportó o identificó una exportación genuina autorizada para esta aceptación. Los MP4 creados por las pruebas están expresamente etiquetados como sintéticos técnicos. En navegador permanecen `Unverified`; no se ha presentado ninguno como aceptación genuina. La prueba de calidad usa una declaración simulada exclusivamente en una base efímera y la identifica como tal.

El operador puede descargar el Video Overview con el control oficial y seleccionar el archivo resultante en el importador; no necesita conocer una ruta interna de Google. La [ayuda oficial de Video Overviews](https://support.google.com/gemininotebook/answer/16454555) sigue documentando esa descarga. Las páginas oficiales de [notebooks Enterprise](https://docs.cloud.google.com/gemini/enterprise/notebooklm-enterprise/docs/api-notebooks) y [fuentes](https://docs.cloud.google.com/gemini/enterprise/notebooklm-enterprise/docs/api-notebooks-sources) se consultaron de nuevo el 2026-10-30. No se ha verificado una API pública soportada para generar/descargar estos vídeos; se conserva la operación manual.

No había claves relevantes de Google en las variables del proceso, usuario o sistema, ni configuración KeyPerFile disponible. El proyecto no declara UserSecretsId; sus appsettings no contienen un cliente OAuth. La base de aceptación anterior consultada no tenía conexiones Google. No se imprimieron credenciales ni se solicitaron por chat. La falta de autorización impide verificar identidad de canal, scopes concedidos, refresh, licencia, notebooks y errores reales. La licencia no se considera inexistente: **no se ha demostrado**.

Para continuar hace falta un vídeo oficial autorizado, configurar privadamente OAuth y conectar la cuenta. Enterprise requiere además licencia/proyecto/IAM válidos. Una subida privada requerirá aprobación explícita para ese vídeo y canal. No se realizaron cambios de cuenta, recursos de pago, subidas, programación ni publicaciones públicas.

## Matriz explícita de capacidades

| Capacidad | Implementado | Probado localmente | Proveedor real | Operación manual | Bloqueo actual |
|---|---|---|---|---|---|
| Acceso, canal, nichos, temas, fuentes y producción | Sí | Sí, API/UI/PostgreSQL | No aplica | Editorial | Ninguno local |
| Paquetes, tareas e historia | Sí | Sí | No para declaraciones NotebookLM | Sí | Validación genuina pendiente |
| Generación y descarga Video Overview | Recorrido manual | Controles de tareas | No | **Sí, interfaz oficial** | Exportación genuina no disponible; API de vídeo no verificada |
| Importación, SHA256, FFprobe, audio/vídeo y reproducción | Sí | Sí, archivos técnicos | **No para NotebookLM genuino** | Seleccionar archivo y declarar procedencia | Exportación genuina pendiente |
| Canónico, miniatura y revisión | Sí | Sí, fixture controlado | No | Revisión del operador | Aceptación con vídeo genuino pendiente |
| OAuth y refresh Google | Sí | Sí, contratos y seguridad | No | Configurar/conectar cuenta | **Bloqueado por credenciales/autorización** |
| Notebooks y fuentes Enterprise | Sí | Sí, contratos controlados | No | Disponible el recorrido oficial manual | **Bloqueado por credenciales; licencia/IAM sin verificar** |
| YouTube, reanudación, metadatos y conciliación | Sí | Sí, contratos controlados | No | Consentimiento explícito | **Bloqueado por credenciales y aprobación del vídeo/canal** |
| Calendario, trabajos y recuperación | Sí | Sí | Publicación real no | Resolver resultados ambiguos | Proveedor pendiente |
| Backup/restore Windows y reinicios | Sí | **Sí, ejecución real** | PostgreSQL local real | Custodiar configuración/copia | Recuperación en otra máquina pendiente |
| Docker/Compose/volúmenes | Configurado | Solo estático | No aplica | Instalar/iniciar Docker | **Bloqueado por entorno** |

La declaración del operador no es prueba criptográfica del proveedor. SHA256 acredita igualdad del archivo conservado, no que Google lo haya generado. Ninguna capacidad externa queda marcada como verificada en vivo.

## Seguridad y operación

Se revisaron autenticación por cookie HttpOnly, CSRF, ausencia de CORS permisivo, filtro Origin/AllowedHosts, estado OAuth de un solo uso con cookie de correlación y PKCE S256, persistencia protegida de tokens, redirecciones oficiales fijas, autorización de `/api`, límites JSON/medios y login, nombres de archivo y rutas por hash, protocolos FFmpeg limitados, restricciones de URL y fuentes no descargadas por la aplicación, procesos sin shell y mensajes de error saneados. Las pruebas confirman rechazo de peticiones sin sesión/CSRF, origen ajeno, estado OAuth inválido, URLs peligrosas, archivo corrupto y acceso a producciones inexistentes.

Se inspeccionaron defaults de producción, logging, claves persistentes y contenedor sin root. Compose publica solamente en loopback, exige autenticación, aplica migraciones separadamente y monta medios/claves/base persistentes. El puerto interno del contenedor no implica exposición pública del host. Ninguna imagen se construyó ni arrancó por ausencia de Docker; sus permisos y volúmenes siguen sin prueba de runtime. Los escáneres de dependencias no sustituyen esta revisión ni acreditan seguridad absoluta.

## Comandos y evidencia reproducible

Los logs locales están en `.audit/acceptance-*.log`, excluidos de Git junto a bases, medios y claves. Las comprobaciones reproducibles se entregan como código y las salidas relevantes se transcriben arriba. No se incluyen cookies, tokens ni backups en Git.

```powershell
# Inspección: árbol limpio, rama y HEAD indicado; remoto coincide.
git status --short
git branch --show-current
git rev-parse HEAD
git ls-remote --heads origin feat/complete-notebook-yt
git switch -c audit/complete-product-acceptance
# Lectura de RTK, README, documentos requeridos y fuentes mediante Get-Content y rg.

# Rutas exactas de las herramientas usadas en este equipo.
$env:PATH = "$env:LOCALAPPDATA\NotebookYt\dotnet;$env:PATH"
$env:NOTEBOOK_TEST_POSTGRES = 'Host=127.0.0.1;Port=55433;Database=postgres;Username=postgres'
$env:TEST_PG_BIN = "$env:LOCALAPPDATA\NotebookYt\pg-minimal\pgsql\bin"
$mediaBin = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Filter ffmpeg.exe -Recurse | Select-Object -First 1).DirectoryName
$env:TEST_FFMPEG = "$mediaBin\ffmpeg.exe"
$env:TEST_FFPROBE = "$mediaBin\ffprobe.exe"
# Esta conexión sin contraseña corresponde SOLO al clúster aislado de pruebas.
& "$env:TEST_PG_BIN\pg_ctl.exe" -D "$env:LOCALAPPDATA\NotebookYt\pg-data-verified" -l "$env:LOCALAPPDATA\NotebookYt\pg-verified.log" -o '-h 127.0.0.1 -p 55433' start

dotnet --version
node --version
npm --version
& "$env:TEST_PG_BIN\pg_dump.exe" --version
& $env:TEST_FFPROBE -version
dotnet build --nologo
dotnet test --no-build --logger 'console;verbosity=minimal'
dotnet list package --vulnerable --include-transitive
cd web
npm ci
npm run typecheck
npm run build
npm test
npm audit --audit-level=low
cd ..

# Publicación local y arranque: ambas se ejecutaron; el script repite publish/build.
dotnet publish src/NotebookYt.Api -o .audit/acceptance-runtime --nologo
.\scripts\Start-LocalAcceptance.ps1 -PgBin $env:TEST_PG_BIN -FfmpegBin $mediaBin -Dotnet "$env:LOCALAPPDATA\NotebookYt\dotnet\dotnet.exe"
$env:PLAYWRIGHT_BASE_URL = 'http://127.0.0.1:5080'
cd web
npm run test:e2e
cd ..
python scripts/Test-LocalPersistence.py --instance .audit/acceptance-055d1053ac804b77aa682341388585b4/instance.json `
  --dotnet "$env:LOCALAPPDATA\NotebookYt\dotnet\dotnet.exe" --pg-bin "$env:TEST_PG_BIN" `
  --pg-data "$env:LOCALAPPDATA\NotebookYt\pg-data-verified" --ffmpeg-bin "$mediaBin" --isolated-postgres

Get-Command docker,docker-compose -ErrorAction SilentlyContinue
Test-Path 'C:\Program Files\Docker\Docker\resources\bin\docker.exe'
Get-Service '*docker*' -ErrorAction SilentlyContinue
# Resultado: sin ejecutables/servicio; Test-Path=False.
# Python/PyYAML verificó servicios, puertos loopback, autenticación y volúmenes del Compose.
Get-NetTCPConnection -State Listen -LocalPort 5080,55433
git diff --check
```

Los scripts de persistencia ejecutan realmente `pg_dump`, `createdb`, `psql`, `pg_restore`, reinicio con `pg_ctl`, parada/arranque de API y consultas HTTP. El test de migraciones ejecuta `IMigrator.MigrateAsync("0")` y `Database.MigrateAsync()`. No se usó `EnsureCreated`. Las operaciones destructivas de estas pruebas se limitan a sus nombres aleatorios conocidos; no se operó sobre bases compartidas.

Incidencias de ejecución, corregidas y no contadas como aprobadas: la primera prueba del script de copia falló por separación de un argumento `--file` en PowerShell; se corrigió y volvió a ejecutar. Una reproducción inicial del fallo Origin coincidió con el reinicio de PostgreSQL y produjo 500; se descartó como evidencia. La reproducción posterior aislada sí dio el 403 esperado, y la suite completa corregida pasó. Dos consultas exploratorias usaron inicialmente un nombre de tabla incorrecto y fallaron; la consulta posterior de `GoogleConnection` fue válida. El sexto login dentro del minuto recibió 429 y pudo reintentarse después; no se debilitó el límite. El harness actual inicia una sesión por worker para no agotarlo con logins redundantes. El primer ensayo de la nueva validación por UI falló por un selector demasiado estricto de un select; se corrigió el locator y la ejecución completa posterior pasó.

## Entrega y requisitos pendientes

Cambios en `Program.cs`, cliente HTTP, pruebas de seguridad/contratos/migraciones/restauración, fixture Playwright, scripts operativos y documentación. No se necesitó migración nueva ni se alteraron datos previos. No hay cambios de motores audiovisuales.

Rama: [audit/complete-product-acceptance](https://github.com/akumanomi1988/Notebook-yt/tree/audit/complete-product-acceptance). Corrección funcional: [b4d0bca](https://github.com/akumanomi1988/Notebook-yt/commit/b4d0bca). El commit posterior añade scripts, pruebas y este informe; ambos se entregan en esa rama, sin merge ni force-push.

Para aceptación completa faltan: vídeo exportado oficialmente y autorizado; configuración privada del operador y OAuth, cuenta/canal y permisos; licencia Enterprise si se quiere usar su API; autorización concreta antes de una subida real privada; Docker disponible y ensayo de sus imágenes/volúmenes. Hasta completarlo no corresponde declarar **COMPLETE AND OPERATIONALLY VERIFIED**.

Los servicios de aceptación se detuvieron tras las comprobaciones. Se conservaron las bases y copias de prueba; no se deja operativa una instancia con la contraseña pública del harness. Para uso diario se requiere el perfil privado del operador descrito en Operaciones.
