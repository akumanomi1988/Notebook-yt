# Notebook-yt

Centro editorial privado para un operador, un canal y siete nichos. NotebookLM es el único origen permitido para el vídeo canónico. La aplicación organiza temas, investigación, paquetes, tareas, archivos, revisión y publicación mediante las APIs oficiales de Google que estén disponibles y autorizadas.

**Activación local:** se ejecutó el recorrido real NotebookLM → descarga oficial → importación → inspección → miniatura → subida privada a YouTube, con procesamiento confirmado. El perfil del operador tiene siete nichos y planificación diaria. La API no devolvió el campo de declaración de IA: se conserva como no confirmado, aunque se envió expresamente. Docker sigue sin verificación de ejecución. Consulta [la activación y pruebas actuales](doc/DAILY_OPERATOR_ACTIVATION.md); los informes anteriores se conservan como evidencia histórica.

## Inicio local

Requisitos: .NET 10 SDK, PostgreSQL 17+, Node 22.12+, npm, FFmpeg y FFprobe. Los ejecutables multimedia deben estar en PATH o configurarse mediante `Media__ffmpegPath` y `Media__ffprobePath`.

1. Crear una base PostgreSQL local dedicada y un usuario con permisos sobre su esquema. Usar autenticación apropiada y limitar PostgreSQL a localhost.
2. Configurar privadamente `ConnectionStrings__Editorial` en la sesión. No versionar contraseñas ni conexiones con secretos.
3. Preparar la contraseña del operador y arrancar:

```powershell
# Desde la raíz. El script solicita la contraseña y la asignación evita imprimir el hash.
$env:Security__PasswordHash = & .\scripts\New-OperatorPasswordHash.ps1
dotnet tool restore
dotnet restore
dotnet ef database update --project src/NotebookYt.Api
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/NotebookYt.Api
```

En otra terminal:

```powershell
cd web
npm ci
npm run dev
```

Abrir `http://127.0.0.1:5173`. La API escucha en `127.0.0.1:5080`. La autenticación está activada por defecto. La clave de sesión y los tokens Google se protegen con ASP.NET Data Protection; conservar su directorio de claves entre reinicios.

Guardar primero el canal en Semana editorial. En Centro de producción, completar Ajustes, aprobar un tema, crear su producción y preparar las fuentes. El trabajador durable se inicia con el backend y funciona aunque el navegador esté cerrado. No hay un trabajador que simule la generación de vídeo.

## Recorrido NotebookLM

1. Registrar fuentes con procedencia y definir el contexto editorial.
2. Congelar el paquete; descargarlo o copiar sus instrucciones.
3. Crear y preparar el notebook por API Enterprise si se dispone de licencia, proyecto y OAuth; en otro caso, realizar las tareas indicadas en la interfaz oficial.
4. Generar Video Overview en NotebookLM y registrar las declaraciones del operador.
5. Descargar con el botón oficial de NotebookLM. El navegador lo guarda en Descargas o pide un destino; no es necesario conocer ninguna ruta interna de Google.
6. Seleccionar el archivo con Importar archivo para inspección. Notebook-yt conserva una copia en su almacenamiento configurable.
7. Un archivo sin declaración de procedencia permanece `Unverified`. La inspección técnica no demuestra que proceda de NotebookLM.
8. Declarar procedencia y autorización del original; completar las tareas, seleccionar el vídeo canónico, extraer o subir una miniatura y revisar calidad y metadatos.
9. Conectar YouTube y aprobar expresamente la subida y su visibilidad/programación. La subida inicial es privada y la publicación se confirma mediante Google.

No hay llamadas privadas, extracción de cookies, scraping autenticado ni otros generadores audiovisuales. No se automatizan la generación ni la descarga de Video Overview sin una API pública soportada y verificada. [Matriz de capacidades](doc/NOTEBOOKLM_CAPABILITIES.md).

## Configuración

| Variable | Función |
|---|---|
| `ConnectionStrings__Editorial` | Conexión PostgreSQL privada |
| `Security__PasswordHash` | Hash PBKDF2 del operador |
| `Security__KeyDirectory` | Claves persistentes de Data Protection |
| `Storage__Root` | Almacén de archivos; por defecto LocalApplicationData/NotebookYt/storage |
| `Media__ffmpegPath`, `Media__ffprobePath` | Ejecutables de inspección y miniaturas |
| `Worker__Enabled` | `true` por defecto; desactivar solo en pruebas o mantenimiento |
| `Security__RequireAuthentication` | `true` por defecto; `false` únicamente en un entorno local de pruebas aislado |
| `Google__ClientId`, `Google__ClientSecret` | Cliente OAuth privado configurado en el servidor |
| `Google__RedirectUri` | Por defecto `http://127.0.0.1:5173/api/studio/google/callback` |
| `NotebookEnterprise__ProjectNumber` | Proyecto numérico autorizado |
| `NotebookEnterprise__Region` | `global`, `us` o `eu` |
| `Hosting__Urls` | Por defecto loopback; en contenedor se usa su red interna con puerto del host limitado a loopback |

El archivo `appsettings.Development.json` mantiene el logging de desarrollo separado del común. No contiene secretos. El endpoint `/health` comprueba el proceso; consultar el canal o la semana comprueba la conexión efectiva a PostgreSQL.

## Pruebas ejecutables

```powershell
# Detener el backend antes de reconstruir en Windows.
dotnet build src/NotebookYt.Api
# Solo reglas/validaciones, sin base de datos ni herramientas de medios:
dotnet test tests/NotebookYt.Tests --filter 'FullyQualifiedName~StudioRulesTests|FullyQualifiedName~ValidationTests|FullyQualifiedName~ProductionPolicyTests'
```

Para toda la suite, configurar `NOTEBOOK_TEST_POSTGRES` con una instancia PostgreSQL aislada cuyo usuario pueda crear bases. Se crean bases aleatorias `notebook_test_<guid>` y se eliminan exclusivamente esas bases al finalizar. FFmpeg y FFprobe deben estar en PATH o indicarse con `TEST_FFMPEG` y `TEST_FFPROBE`. En Windows, indicar también `TEST_PG_BIN` con el directorio de `pg_dump.exe`, `pg_restore.exe`, `psql.exe` y `createdb.exe` para la restauración real; ese caso comprueba ACL y DPAPI y se omite fuera de Windows.

```powershell
dotnet test tests/NotebookYt.Tests
cd web
npm run typecheck
npm test
npm run build
npm audit
npx playwright install chromium
npm run test:e2e
```

E2E requiere la instancia autenticada exclusiva de pruebas de `Start-LocalAcceptance.ps1`, con `Security__RequireAuthentication=true`. Usar `PLAYWRIGHT_BASE_URL=http://127.0.0.1:5080` para el frontend empaquetado; el valor predeterminado `5173` permite probar Vite contra la misma API. La suite inicia sesión por la interfaz y conserva autenticación, CSRF y límites de intentos. Modifica canal y nichos y crea datos de prueba; no usar datos de trabajo. Consulta el procedimiento en [Operaciones](doc/OPERATIONS.md).

Los archivos sintéticos se usan exclusivamente como fixtures técnicos en pruebas. Nunca acreditan procedencia real de NotebookLM ni aceptación externa. Los contratos de Google se prueban mediante HTTP controlado; no se realizan subidas reales en la suite.

## Operación y persistencia

Hay tres migraciones acumulativas, conservando el historial auditado. `--migrate` aplica migraciones explícitamente y termina sin arrancar el servidor. No se usa `EnsureCreated`.

Los estados editoriales iniciales siguen disponibles. El espacio de trabajo ampliado mantiene etapas, tareas, trabajos y publicación separados. Los snapshots editoriales, paquetes, revisiones y eventos son inmutables. Las escrituras de la API y las reclamaciones de trabajos se coordinan con PostgreSQL; los trabajos usan propietario, lease renovable, recuperación y reintentos acotados.

Crear una producción admite `Idempotency-Key`: misma clave y cuerpo devuelve la misma producción; reutilizar la clave con otro cuerpo devuelve 409. La creación desde un tema, creación de notebook y subida tienen claves durables propias. El resultado ambiguo de una operación externa requiere conciliación; nunca se repite a ciegas.

Los paquetes se congelan para conservar fuentes y contexto. Para una investigación editorial distinta, crear otra producción. Los archivos importados se conservan por checksum; nuevas versiones tienen registros independientes y reemplazar el canónico invalida la aprobación. La limpieza solo elimina archivos huérfanos antiguos, nunca archivos referenciados ni historial.

El planificador conserva fecha y hora local, zona IANA y UTC. Los saltos DST se desplazan al primer minuto válido; en una hora repetida se elige la ocurrencia posterior. La pausa detiene la creación de turnos, no elimina trabajos ya aprobados. Los turnos vencidos quedan como `Missed` y exigen revisión.

[Docker, OAuth, copias, restauración y diagnóstico](doc/OPERATIONS.md). [Informe de pruebas y límites de aceptación](doc/COMPLETE_PRODUCT_REPORT.md).
