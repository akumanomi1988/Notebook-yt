# Operación local

## Entorno nativo

Seguir README. Conservar juntos PostgreSQL, `Storage__Root` y `Security__KeyDirectory`. Los secretos deben permanecer en variables privadas o archivos fuera del repositorio. No guardar conexiones o contraseñas en documentos de pruebas ni en capturas.

Notebook-yt no tiene autenticación propia ni depende de una contraseña de operador. La API y PostgreSQL deben escuchar exclusivamente en `127.0.0.1`. No habilitar CORS general: el servidor valida `Origin` y exige un token antifalsificación para cada escritura de API. Esta configuración es solo para uso en el equipo; cualquier despliegue remoto requiere una decisión de acceso independiente.

Windows protege las claves de Data Protection con DPAPI del usuario. Linux exige conservar el volumen de claves con permisos exclusivos; su directorio se limita a 0700. Estas claves protegen datos de integraciones y no representan una sesión del operador. Proteger también el dispositivo o volumen de almacenamiento.

## Docker Compose

El backend ahora fija su socket en `127.0.0.1` y rechaza cualquier otra dirección. El Compose actual no ha sido adaptado ni validado para ese contrato de loopback dentro del contenedor; usar el entorno nativo descrito en README. No exponer un contenedor mediante cambios a `0.0.0.0`.

Se proporciona Dockerfile multietapa y `compose.yaml`. El contenedor incluye el frontend compilado y FFmpeg; el trabajador forma parte del backend. Los puertos del host están limitados a 127.0.0.1. No exponerlos públicamente.

Antes de arrancar, crear dos archivos privados fuera del repositorio y configurar sus rutas:

- `POSTGRES_PASSWORD_FILE`: contraseña local del usuario PostgreSQL.
- `EDITORIAL_CONNECTION_FILE`: conexión del contenedor, usando el host de servicio `postgres`, puerto 5432, base/usuario `notebookyt` y la contraseña anterior.

```powershell
docker compose up --build -d
```

El servicio `migrate` aplica migraciones y termina; `app` depende de que complete correctamente. El proceso del contenedor usa el usuario 1654. Los datos PostgreSQL, medios y claves tienen volúmenes separados. Abrir `http://127.0.0.1:5080` para la versión empaquetada. El puerto del host está ligado solo a loopback; las escrituras mantienen validación de origen y CSRF.

**La ejecución Docker no se verificó en este equipo porque Docker no está disponible.** El YAML se analizó correctamente; esto no demuestra que la imagen o sus volúmenes estén probados. No usar `docker compose down -v` sobre datos que deban conservarse.

## Google OAuth

Configurar un cliente OAuth de aplicación web en un proyecto propio con las APIs necesarias habilitadas. No es una operación que esta entrega haya realizado. Registrar exactamente el callback usado por la aplicación:

- Desarrollo Vite: `http://127.0.0.1:5173/api/studio/google/callback`.
- Versión empaquetada local: `http://127.0.0.1:5080/api/studio/google/callback`, configurando `Google__RedirectUri` con ese valor.

Configurar `Google__ClientId` y `Google__ClientSecret` de forma privada en el servidor. Conectar desde Integraciones. OAuth verifica state de un solo uso, cookie de correlación y PKCE. Las credenciales no pasan al frontend y no se registran las URLs de sesión reanudable.

YouTube solicita scopes de subida, consulta propia y actualización del vídeo. El canal autorizado se identifica mediante `channels.list`. La integración no elude verificaciones, cuotas ni políticas de Google. Una cuenta conectada no demuestra por sí sola que pueda publicar o programar cualquier vídeo.

Enterprise necesita además proyecto numérico, región, licencia y roles IAM. La declaración de licencia en Ajustes no equivale a una comprobación de Google. Las operaciones solo quedan verificadas cuando las confirma el proveedor; las declaraciones manuales permanecen diferenciadas.

## Trabajos y recuperación

El backend reclama un trabajo con coordinación PostgreSQL, propietario y lease. Renueva el lease cada 30 segundos mientras procesa. Una interrupción de inspección o de consulta puede recuperarse. Los errores transitorios tienen hasta cinco intentos con espera exponencial. Una operación ambigua de creación externa no se repite automáticamente.

- Notebook creado con respuesta perdida: localizarlo en la interfaz oficial, registrar su URL, verificarlo o completar la tarea con evidencia y conciliar el trabajo.
- Fuente registrada con ID: se consulta ese mismo ID; no vuelve a crearse. Una respuesta perdida sin ID exige revisión manual.
- Subida interrumpida con sesión guardada: se pregunta el rango confirmado a YouTube antes de enviar más bytes.
- Resultado de inicialización desconocido: no se crea una nueva subida. Conciliar el vídeo existente con evidencia y confirmación de su canal cuando corresponda.
- Trabajo activo: no se puede cancelar a mitad de una operación externa. Esperar al paso acotado y revisar su resultado.
- Vídeo subido: las revisiones de metadatos y programación tienen aprobación explícita y eventos con valores anteriores y nuevos.
- Programación vencida: no se aplica automáticamente una fecha pasada que pueda publicar inmediatamente. Se detiene para revisión.

La pausa del planificador no revoca una publicación ya aprobada. Para un vídeo ya registrado en YouTube, revisar y modificar expresamente su programación mediante el control de publicación o la interfaz oficial.

Una fecha de publicación futura programa su paso a público en YouTube. Es obligatorio seleccionar `public` expresamente; `private` o `unlisted` con fecha se rechazan con 409. Para mantener esas visibilidades, borrar la fecha. Un turno editorial puede proponer una fecha, pero no sustituye esa elección explícita. Los trabajos antiguos contradictorios se detienen antes de llamar al proveedor y requieren revisión.

## Archivos, copias y restauración

Los archivos se escriben en staging, se calcula SHA256, se sincronizan y se renombran a una clave derivada del checksum. Un fallo entre archivo y transacción puede dejar un huérfano; no una referencia a un archivo que aún se esté escribiendo. La comprobación de almacenamiento muestra ausencias y huérfanos. La limpieza solo elimina staging o blobs no referenciados más antiguos que la retención configurada. No elimina historia ni medios referenciados.

Para una copia coherente:

1. Detener aplicación y trabajador.
2. Ejecutar `pg_dump` de la base dedicada usando las credenciales privadas.
3. Copiar íntegramente los directorios/volúmenes de medios y claves de protección, conservando sus permisos.
4. Guardar también la configuración privada por separado mediante un sistema seguro.
5. Reiniciar y comprobar salud y una producción con sus archivos.

Restaurar primero en una base y directorios nuevos y aislados; no sobrescribir una base compartida. Restaurar el dump, medios y claves, configurar el mismo usuario/entorno de protección cuando se use DPAPI, aplicar migraciones pendientes y ejecutar la comprobación de almacenamiento. La aceptación posterior ejecutó una restauración real en Windows: todas las filas, checksums, ACL y descifrado de un valor protegido de prueba, además del arranque de la aplicación restaurada. Consulta [la evidencia de aceptación](OPERATIONAL_ACCEPTANCE_REPORT.md).

### Copia y restauración reproducibles en Windows

Los scripts requieren PowerShell, PostgreSQL local y el mismo usuario/máquina cuando las claves estén protegidas con DPAPI. Usar `.pgpass`/`PGPASSFILE` privado o el mecanismo de credenciales aprobado; no poner contraseñas en argumentos ni en el repositorio. Los directorios de destino y la base de restauración deben ser nuevos. El usuario PostgreSQL necesita permiso para crear esa base aislada.

```powershell
# Detener API y trabajador antes de copiar. Sustituir estas rutas por las configuradas.
.\scripts\Backup-NotebookYt.ps1 -Database notebookyt -Server 127.0.0.1 -Port 5432 -Username notebookyt `
  -PgBin 'C:\PostgreSQL\bin' -MediaDirectory 'C:\NotebookYt\media' -KeyDirectory 'C:\NotebookYt\keys' `
  -Destination 'C:\NotebookYtBackups\acceptance-20261030' -ApplicationStopped

.\scripts\Restore-NotebookYt.ps1 -BackupDirectory 'C:\NotebookYtBackups\acceptance-20261030' `
  -NewDatabase notebookyt_restore_20261030 -Server 127.0.0.1 -Port 5432 -Username notebookyt `
  -PgBin 'C:\PostgreSQL\bin' -MediaDirectory 'C:\NotebookYtRestore\media' -KeyDirectory 'C:\NotebookYtRestore\keys'
```

El backup incluye dump en formato custom, medios, claves, SHA256 y descriptores de permisos Windows. Su directorio queda restringido al usuario que lo crea. El manifiesto guarda **nombres** de configuración, sin sus valores. Conservar la configuración privada por separado en un almacén seguro. Un SHA256 permite detectar corrupción; no autentica un backup frente a alguien que pueda modificar también el manifiesto. Guardar las copias fuera del equipo con protección adecuada sigue siendo una tarea del operador.

Configurar el entorno restaurado con `ConnectionStrings__Editorial` apuntando a la **base nueva**, `Storage__Root` y `Security__KeyDirectory` apuntando a los directorios restaurados. Reponer privadamente la configuración OAuth si se necesita y conservar la identidad Windows/máquina cuando se use DPAPI. Arrancar primero aislado, comprobar el endpoint CSRF, semana, producciones, tareas, archivos y almacenamiento. No conectar dos trabajadores a la misma copia ni realizar publicaciones durante el ensayo. No se ha verificado recuperación en otra máquina, Linux ni volúmenes Docker.

### Ensayo local desechable sin login de operador

`Start-LocalAcceptance.ps1` compila, crea una base aleatoria, aplica migraciones y sirve el frontend empaquetado en loopback. Requiere un PostgreSQL de pruebas ya iniciado; está diseñado para la instancia aislada local con usuario `postgres`. Nunca usar esa base para datos operativos.

```powershell
.\scripts\Start-LocalAcceptance.ps1 -PgBin $env:TEST_PG_BIN -FfmpegBin (Split-Path $env:TEST_FFMPEG) -Dotnet dotnet
$env:PLAYWRIGHT_BASE_URL = 'http://127.0.0.1:5080'
cd web
npm run test:e2e
cd ..
# Usar el instance.json que imprime el script. --pg-data debe ser exclusivamente de pruebas.
python scripts/Test-LocalPersistence.py --instance '<acceptance-directory>/instance.json' --dotnet dotnet `
  --pg-bin "$env:TEST_PG_BIN" --pg-data '<isolated-postgres-data>' --ffmpeg-bin '<ffmpeg-bin>' --isolated-postgres
```

El ensayo compara respuestas, historias y hashes antes/después de reiniciar API y PostgreSQL, restaura en otra base/directorios y arranca esa copia. Solo usa medios técnicos de prueba. Conserva los datos para inspección; detener después el PID registrado en `instance.json` y la instancia PostgreSQL de pruebas. No elimina bases ni volúmenes existentes. Las E2E requieren una base de ensayo limpia y comprueban CSRF y rechazo de orígenes ajenos sin login.

## Diagnóstico

- 400 por CSRF: recargar la aplicación para renovar el token; 403 por origen: abrir desde `127.0.0.1` o el frontend local permitido.
- 409: revisar estado, tarea anterior, paquete congelado, origen no declarado o aprobación pendiente.
- `PendingInspection`: comprobar el trabajador y las rutas de FFmpeg/FFprobe.
- `Rejected`: consultar error de validación; el archivo no puede ser canónico.
- `Unverified`: el archivo puede inspeccionarse, pero no aprobarse como vídeo NotebookLM sin declaración y referencia.
- `NeedsAttention`: conciliar el resultado externo; no crear otra operación a ciegas.
- Error de copia al compilar en Windows: detener el proceso API antes de reconstruir.
- Sin acceso a Google: completar el recorrido manual permitido; no usar cookies ni endpoints privados.

No se verificaron despliegues remotos, proxy TLS, Docker ni funcionamiento de una cuenta Google real. La restauración Windows local sí tiene evidencia en la aceptación posterior. Cualquier despliegue privado remoto requiere HTTPS, configuración explícita del proxy/orígenes y revisión operativa adicional. No se desplegó infraestructura ni se incurrió en costes.
