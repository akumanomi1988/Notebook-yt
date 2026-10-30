# Operación local

## Entorno nativo

Seguir README. Conservar juntos PostgreSQL, `Storage__Root` y `Security__KeyDirectory`. Los secretos deben permanecer en variables privadas o archivos fuera del repositorio. No guardar conexiones o contraseñas en documentos de pruebas ni en capturas.

La autenticación está activada por defecto. Para establecer el hash del operador en una sesión PowerShell:

```powershell
$env:Security__PasswordHash = & .\scripts\New-OperatorPasswordHash.ps1
```

Persistir después ese valor mediante un mecanismo privado del sistema, sin imprimirlo en informes. El script exige una contraseña de al menos 12 caracteres y usa PBKDF2 con sal aleatoria y 600.000 iteraciones. No existe un usuario ni contraseña por defecto. La desactivación de autenticación solo se usó contra bases locales de pruebas.

Windows protege las claves con DPAPI del usuario. Linux exige conservar el volumen de claves con permisos exclusivos; su directorio se limita a 0700. Proteger también el dispositivo o volumen de almacenamiento. Perder las claves puede impedir descifrar credenciales o restaurar sesiones.

## Docker Compose

Se proporciona Dockerfile multietapa y `compose.yaml`. El contenedor incluye el frontend compilado y FFmpeg; el trabajador forma parte del backend. Los puertos del host están limitados a 127.0.0.1. No exponerlos públicamente.

Antes de arrancar, crear tres archivos privados fuera del repositorio y configurar sus rutas:

- `POSTGRES_PASSWORD_FILE`: contraseña local del usuario PostgreSQL.
- `EDITORIAL_CONNECTION_FILE`: conexión del contenedor, usando el host de servicio `postgres`, puerto 5432, base/usuario `notebookyt` y la contraseña anterior.
- `OPERATOR_PASSWORD_HASH_FILE`: hash del operador obtenido con el script.

```powershell
docker compose up --build -d
```

El servicio `migrate` aplica migraciones y termina; `app` depende de que complete correctamente. El proceso del contenedor usa el usuario 1654. Los datos PostgreSQL, medios y claves tienen volúmenes separados. Abrir `http://127.0.0.1:5080` para la versión empaquetada. La autenticación permanece activada.

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

## Archivos, copias y restauración

Los archivos se escriben en staging, se calcula SHA256, se sincronizan y se renombran a una clave derivada del checksum. Un fallo entre archivo y transacción puede dejar un huérfano; no una referencia a un archivo que aún se esté escribiendo. La comprobación de almacenamiento muestra ausencias y huérfanos. La limpieza solo elimina staging o blobs no referenciados más antiguos que la retención configurada. No elimina historia ni medios referenciados.

Para una copia coherente:

1. Detener aplicación y trabajador.
2. Ejecutar `pg_dump` de la base dedicada usando las credenciales privadas.
3. Copiar íntegramente los directorios/volúmenes de medios y claves de protección, conservando sus permisos.
4. Guardar también la configuración privada por separado mediante un sistema seguro.
5. Reiniciar y comprobar salud y una producción con sus archivos.

Restaurar primero en una base y directorios nuevos y aislados; no sobrescribir una base compartida. Restaurar el dump, medios y claves, configurar el mismo usuario/entorno de protección cuando se use DPAPI, aplicar migraciones pendientes y ejecutar la comprobación de almacenamiento. No se ejecutó una restauración real de backup en esta sesión; sí se verificó persistencia tras reiniciar API y PostgreSQL.

## Diagnóstico

- 401: iniciar sesión; 400 por CSRF: recargar para renovar el formulario.
- 409: revisar estado, tarea anterior, paquete congelado, origen no declarado o aprobación pendiente.
- `PendingInspection`: comprobar el trabajador y las rutas de FFmpeg/FFprobe.
- `Rejected`: consultar error de validación; el archivo no puede ser canónico.
- `Unverified`: el archivo puede inspeccionarse, pero no aprobarse como vídeo NotebookLM sin declaración y referencia.
- `NeedsAttention`: conciliar el resultado externo; no crear otra operación a ciegas.
- Error de copia al compilar en Windows: detener el proceso API antes de reconstruir.
- Sin acceso a Google: completar el recorrido manual permitido; no usar cookies ni endpoints privados.

No se verificaron despliegues remotos, proxy TLS, Docker, restauración completa ni funcionamiento de una cuenta Google real. Cualquier despliegue privado remoto requiere HTTPS, configuración explícita del proxy/orígenes y revisión operativa adicional. No se desplegó infraestructura ni se incurrió en costes.
