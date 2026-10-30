# Notebook-yt — Phase 1

Gestión editorial local para un operador, un canal y siete nichos evergreen. Incluye calendario semanal, edición y desactivación de nichos, creación y edición de producciones, filtros, estados auditables y registro manual de referencias NotebookLM. No genera vídeos ni publica contenido.

## Requisitos

- .NET 10 SDK.
- PostgreSQL local. Verificado con PostgreSQL 17.6.
- Node.js 22.12 o posterior; verificado con Node 24.22.1 y npm 11.19.1.
- Chromium para las pruebas E2E: `npx playwright install chromium` desde `web`.

## Inicio local reproducible

Crear una base vacía dedicada y un usuario local con permisos de esquema en esa base. PostgreSQL debe escuchar solo en localhost. Para desarrollo habitual, usar autenticación con contraseña y un usuario sin privilegios de superusuario. No reutilizar una base compartida para las pruebas.

En PowerShell, desde la raíz, con la variable `ConnectionStrings__Editorial` configurada de forma privada en la sesión:

```powershell
dotnet tool restore
dotnet restore
dotnet ef database update --project src/NotebookYt.Api
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/NotebookYt.Api
```

La conexión usa las claves Npgsql `Host`, `Port`, `Database`, `Username` y, cuando corresponda, `Password`. No guardar su valor en archivos versionados, capturas ni informes. Las migraciones están incluidas; no ejecutar `EnsureCreated` ni generar otra migración inicial.

En otra terminal:

```powershell
cd web
npm ci
npm run dev
```

Abrir `http://127.0.0.1:5173`. La API escucha en `http://127.0.0.1:5080`; `/health` indica que el proceso responde. La conexión efectiva a PostgreSQL se verifica mediante `/api/channel` y `/api/week`. Antes de configurar el canal, `/api/channel` devuelve JSON `null` y la semana es una lista vacía. Guardar el canal crea las siete asignaciones de forma atómica.

## Comprobaciones

Detener la API antes de reconstruir en Windows para evitar el bloqueo del ejecutable.

```powershell
dotnet build src/NotebookYt.Api
# Solo pruebas unitarias, sin PostgreSQL:
dotnet test tests/NotebookYt.Tests --filter 'FullyQualifiedName!~ApiIntegrationTests'
```

Para la suite completa, configurar `NOTEBOOK_TEST_POSTGRES` con una conexión a una instancia PostgreSQL local y aislada. El usuario de esa conexión necesita permiso para crear bases. Las pruebas crean una base aleatoria `notebook_test_<guid>`, aplican migraciones, ejecutan HTTP y SQL y eliminan únicamente esa base al terminar.

```powershell
dotnet test tests/NotebookYt.Tests
cd web
npm run typecheck
npm run build
npm test
npm audit
```

Para E2E, iniciar API y Vite con una base exclusiva de pruebas, migrada, y ejecutar desde `web`:

```powershell
npx playwright install chromium
npm run test:e2e
```

Las E2E modifican el canal y el nicho del lunes y crean producciones de prueba. No ejecutarlas contra datos de trabajo. Cubren creación y edición real, estados, filtros, URL inválida y válida, cancelación, carga fallida y recuperación, móvil y foco de teclado. Las capturas se guardan en `web/test-results/`, ignorado por Git.

## Contratos y decisiones

- `GET/PUT /api/channel`: identidad persistente de un único canal; zona IANA.
- `GET /api/week`, `PUT /api/week/{weekday}`: días ISO 1–7. No hay operaciones para crear o eliminar asignaciones individuales.
- `GET/POST /api/productions`: listado y creación. Filtros API opcionales `status=Draft` y `nicheId=<guid>`. La interfaz filtra el conjunto local cargado.
- `GET/PUT /api/productions/{id}`: detalle y edición de tema/fecha. El nicho y su snapshot no son editables.
- `POST /api/productions/{id}/status`: estado textual; transiciones inválidas devuelven 409.
- `PUT /api/productions/{id}/notebook`: actualiza una referencia por producción. Repetir la operación conserva su identidad.
- Validaciones 400, recurso inexistente 404, conflicto 409 y fallos inesperados 500 mediante Problem Details. Las respuestas públicas usan DTOs.
- Política central en `ProductionPolicy`. El siguiente paso manual es un identificador estable devuelto por el backend y traducido por la interfaz.
- Snapshot requerido en Phase 1: nombre del nicho al crear la producción. Renombrar o desactivar el nicho conserva las producciones anteriores.
- `PlannedDate` usa SQL `date`. Creación, modificación y auditoría usan UTC/`timestamptz`. La fecha inicial de la UI respeta la zona del canal.
- Las escrituras se serializan mediante bloqueo transaccional asesor de PostgreSQL, compartido entre instancias de la API. La transacción confirma antes de enviar el cuerpo de éxito. Esta estrategia prioriza corrección para un solo operador.
- PostgreSQL impone canal único, días únicos y válidos, siete asignaciones al confirmar, FK restrictivas y compuestas, estados válidos e inmutabilidad del nombre histórico. No escribir directamente en las tablas: las transiciones y su auditoría se realizan por la API.

## Seguridad y límites

Uso local sin autenticación. No publicar esta API ni Vite en Internet. Se limitan los hosts y orígenes de navegador; no hay CORS abierto. Los ajustes comunes y de desarrollo están separados, sin secretos. `/health` es una comprobación del proceso, no de disponibilidad de la base.

POST de creación no es idempotente: repetirlo crea otra producción. La interfaz bloquea envíos simultáneos. El registro de notebook sí actualiza una única referencia. No hay paginación ni soporte multioperador, y no se automatizan operaciones de Google.

La aceptación y las verificaciones ejecutadas están en [PHASE1_COMPLETION.md](doc/PHASE1_COMPLETION.md). La [auditoría anterior](doc/PHASE1_AUDIT.md) conserva el diagnóstico histórico anterior a esta implementación. La viabilidad actual de Google se documenta en [NOTEBOOKLM_FEASIBILITY.md](doc/NOTEBOOKLM_FEASIBILITY.md).
