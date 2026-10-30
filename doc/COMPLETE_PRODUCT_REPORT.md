# Implementación del producto Notebook-yt — 2026-10-30

## Veredicto

Este informe corresponde al commit de implementación `485c0d8`. La verificación independiente posterior, con autenticación y restauración reales, se documenta en [la aceptación operativa](OPERATIONAL_ACCEPTANCE_REPORT.md). Sus resultados actualizados no convierten las pruebas controladas en verificación de Google real.

**INCOMPLETE.** Se entregan código ejecutable y pruebas locales de la aplicación ampliada. La aceptación completa exigida no puede declararse: falta una exportación autorizada y genuina de NotebookLM, acceso OAuth/licencia de esta cuenta para verificar Google en vivo y ejecución de Docker en un entorno que lo tenga disponible. No se presenta un vídeo técnico sintético como sustituto ni se atribuyen a NotebookLM resultados de otra procedencia.

Las limitaciones externas no detuvieron el trabajo independiente: se implementaron los módulos locales, los adaptadores oficiales utilizables y los recorridos manuales. No se afirma generación desatendida ni publicación real verificada.

## 1. Funcionalidad implementada

Canal y siete nichos con normas ampliadas; temas propuestos/aprobados/rechazados, prioridad y duplicados; creación idempotente de producciones; fuentes URL, YouTube, notas y documentos; paquetes congelados; espacio NotebookLM y tareas manuales secuenciales; importación, inspección, versiones y reproducción de archivos; miniaturas por fotograma con recorte/texto o carga manual; controles de calidad; revisión editorial; metadatos, aprobación y cola YouTube; calendario mensual/semanal, planificación durable, incidencias y recuperación.

Se conserva la funcionalidad auditada de Phase 1 y su historia. El nuevo trabajo no se entregó como otra fase ni sustituyó el motor audiovisual.

## 2–4. Arquitectura y módulos

Monolito ASP.NET Core/EF/PostgreSQL y React/TypeScript/Vite. La API anterior mantiene sus contratos. El espacio ampliado separa estado editorial inicial, etapa de trabajo, tarea manual, estado de archivo, trabajo y publicación.

Backend en `src/NotebookYt.Api/Studio/`:

- `StudioDomain`, `StudioModel`: entidades y relaciones.
- `StudioRules`, `StudioEndpoints`: validaciones, tareas, evidencias y operaciones editoriales.
- `EditorialScheduler`: rotación, horarios y DST.
- `StudioWorker`: propietario, lease renovable, reintentos y recuperación.
- `MediaStorage`, `QualityGate`: almacenamiento, inspección y bloqueos.
- `OperatorSecurity`: sesión, contraseña, CSRF y limitación de acceso.
- `GoogleAuthorization`: OAuth, PKCE, state y credenciales protegidas.
- `NotebookIntegration`: contratos Enterprise documentados.
- `YouTubePublishing`: sesión reanudable, miniatura, metadatos, verificación y conciliación.

Frontend: `AuthGate`, cliente HTTP con CSRF, interfaz anterior conservada y `Studio` con Resumen, Calendario, Temas, Producciones, Trabajos, Integraciones y Ajustes. Cada producción ofrece Investigación, NotebookLM, Vídeo y calidad, Publicación e Historial. No hay estadísticas de tendencia inventadas ni un trabajador de generación simulado.

## 5. Persistencia y contratos

Migración añadida: `CompleteStudio`, sobre las dos migraciones anteriores. Incluye ajustes, temas, espacios de trabajo, fuentes, paquetes, tareas, archivos, revisiones, eventos, turnos, trabajos, conexiones, intentos OAuth, correspondencias de fuentes y recibos de idempotencia.

FK restrictivas, índices únicos y checks protegen canal/nichos, temas, archivos, tareas y operaciones. Los triggers conservan snapshots, paquetes, eventos, revisiones e historial. Los binarios quedan fuera de PostgreSQL.

Snapshot: nombre histórico anterior conservado; al abrir el espacio se captura el contexto editorial disponible y se congela. Para producciones anteriores no se inventa una descripción histórica que nunca se guardó. El paquete conserva sus fuentes y contexto; una investigación distinta requiere otra producción. Los archivos nuevos son versiones independientes; los bytes idénticos se deduplican por SHA256.

Idempotencia: clave opcional de creación API con comparación del cuerpo, obligatoria en el cliente de creación; producción por tema única; job keys durables; una subida por producción; una referencia por producción; un binding por fuente. Operaciones ambiguas externas se concilian, no se repiten a ciegas.

## 6–8. NotebookLM

La [matriz detallada](NOTEBOOKLM_CAPABILITIES.md) diferencia DOCUMENTED, IMPLEMENTED, LOCALLY TESTED, LIVE VERIFIED, MANUAL ONLY y UNVERIFIED. Ninguna operación de esta cuenta se declara LIVE VERIFIED en esta sesión.

Implementados: creación y consulta de notebook Enterprise, incorporación de texto/web/YouTube, subida de documentos y consulta del procesamiento de fuentes. Los contratos de creación, texto y consulta se probaron con respuestas HTTP controladas. La variante binaria de fuentes también tiene contrato HTTP positivo ejecutado; ninguna variante se ejecutó en vivo con esta cuenta.

La generación, revisión de notas de productor, confirmación del resultado y descarga de Video Overview se realizan en la interfaz oficial cuando no hay API pública soportada verificada. Las opciones de formato/estilo no se inventan: se eligen en la cuenta real de NotebookLM. Las tareas registran responsable, instrucciones, resultado esperado, evidencia y marcas UTC. Una declaración del operador no se convierte en evidencia del proveedor.

El botón Download lo ejecuta el operador en NotebookLM; el navegador elige su carpeta de descarga o pide destino. Luego se importa el archivo en Notebook-yt. No se necesita una ruta interna de Google. No se usaron cookies, sesiones privadas, scraping ni otro generador audiovisual.

Las operaciones administrativas de listar/eliminar/compartir notebooks no se exponen en esta aplicación; no se realizaron modificaciones ni comparticiones externas. Esto limita la superficie del adaptador respecto de toda la API Enterprise y se documenta, sin afirmar una cobertura universal de Google.

## 9. Archivos de vídeo

Importación por streaming, límite de tamaño, MIME, clave interna sin rutas del cliente, escritura staging, SHA256, flush y renombrado. FFprobe inspecciona contenedor/códec/audio/duración/resolución y FFmpeg prueba la decodificación. Se restringen protocolos y se rechazan archivos corruptos.

`Unverified` puede inspeccionarse y reproducirse, pero no ser canónico. La procedencia `OperatorAttested` exige declaración, evidencia y notebook asociado. No constituye prueba criptográfica de Google. No existe una ruta que finja origen `ProviderVerified` sin evidencia oficial.

El canónico exige tareas completadas y archivo válido. La revisión añade bloqueos de duración, resolución, audio, paquete, notebook, miniatura y metadatos. Las miniaturas derivan de fotogramas o archivos manuales. Recorte y texto son deterministas. La limpieza respeta la retención y nunca elimina archivos referenciados ni historia.

## 10. YouTube

OAuth con state de un solo uso, PKCE, identificación de canal, token refresh y almacenamiento protegido. El adaptador usa subida reanudable oficial, conserva la URL cifrada y consulta el rango antes de continuar. Los bloques son de 8 MiB salvo el último. La sesión ambigua no crea otra subida.

La subida inicial es privada. La aplicación aplica miniatura, metadatos y visibilidad/programación aprobadas. Las revisiones posteriores preservan eventos con valores anteriores/nuevos. La programación pasada o de un vídeo ya publicado no se acepta localmente. Google puede imponer restricciones adicionales.

`Published` exige consulta oficial, canal correspondiente y estado procesado/público. El contrato controlado prueba que la intención local no basta. No se ejecutó subida, programación ni publicación reales. También se ejecutó el contrato positivo de miniatura, actualización de metadatos y programación futura privada. Es una respuesta controlada, no una aceptación autorizada de la cuenta real.

## 11. Planificación y automatización

Calendario con nichos por día y producciones; turnos durables de una rotación de siete días; horizonte/antelación; asignación de temas aprobados; pausa, reprogramación, conflictos y turnos vencidos. Se guardan fecha/hora local, zona y UTC. DST: huecos se desplazan al primer minuto válido y solapamientos usan la ocurrencia posterior.

El trabajador vive en el backend, no en el navegador. Coordinación PostgreSQL, propietario, lease renovado cada 30 segundos, recuperación, timeout multimedia, reintentos acotados y backoff. Las tareas manuales y la falta de archivo suspenden la automatización dependiente. Las verificaciones de YouTube se espacian y tienen plazo; no se inventa un saldo de cuota.

Las pruebas verifican recuperación de lease vencido en inspección, detención de creación externa ambigua e idempotencia del planificador. No se hizo una prueba de carga prolongada con múltiples procesos ni una interrupción forzada durante una llamada real a Google.

## 12. Seguridad

Autenticación activada por defecto; operador administrador único; hash PBKDF2; cookie HttpOnly/SameSite; CSRF; rate limit de login; límite JSON de 1 MB y límites de medios. Orígenes/hosts locales restringidos. Tokens y sesiones de subida protegidos con Data Protection, claves persistentes, DPAPI en Windows y directorio 0700 en Linux. Clientes HTTP sin logging de credenciales ni redirecciones automáticas. No se fetchean URLs de investigación desde la red privada del servidor.

Las pruebas de sesión/CSRF se ejecutan con autenticación activada. El servidor E2E usó autenticación desactivada expresamente contra la base de pruebas aislada. No es una configuración de despliegue.

No se desplegó públicamente, activó facturación ni contrató infraestructura. Las auditorías finales npm y NuGet no encontraron vulnerabilidades conocidas en las fuentes consultadas. No equivale a un pentest completo ni a la verificación de un proxy/TLS remoto.

## 13. Comandos y resultados

Se agrupan las lecturas y comandos repetidos de desarrollo. No se incluyen valores de secretos porque no se proporcionaron credenciales reales.

| Comando / operación | Resultado |
|---|---|
| `git status --short`, `git branch -a`, `git log -6`, `git cat-file -t 46fd966`, `rg --files`, lecturas de README/auditoría/fuentes/configuración/migraciones/pruebas | Base limpia `6111e83`, audit commit presente e historial preservado |
| `git switch -c feat/complete-notebook-yt`; consulta de permisos de push con gh | Rama creada; permiso de escritura confirmado |
| `.NET --version`, `node --version`, `npm --version`, búsqueda FFmpeg/FFprobe y Docker | SDK 10.0.403, Node 24.22.1, npm 11.19.1; FFmpeg/FFprobe 9.0.2 disponibles; Docker no disponible |
| `winget install --id Gyan.FFmpeg ...` | Ya estaba instalado; no había actualización |
| `dotnet test tests/NotebookYt.Tests` al comenzar | 18/18 de la base anterior, ejecutadas de nuevo |
| `dotnet build src/NotebookYt.Api` durante desarrollo | Detectó métodos de extensión en clases no estáticas; corregidos; builds posteriores sin errores |
| `dotnet ef migrations add CompleteStudio --project src/NotebookYt.Api` | Migración generada y ampliada con triggers de inmutabilidad |
| `createdb ... complete_studio_acceptance`; `dotnet ef database update` | Tres migraciones aplicadas en base nueva |
| `dotnet test ...` durante incorporación de pruebas | Detectó escapes SQL de un fixture y una conexión Google duplicada entre pruebas; corregidos |
| `dotnet test tests/NotebookYt.Tests` final | **41/41**, cero omitidas |
| `npm run typecheck` durante desarrollo | Detectó una opción inválida de Testing Library; corregida |
| `npm run typecheck`; `npm run build` finales | TypeScript y Vite correctos |
| `npm test` | **10/10**, cuatro archivos de pruebas |
| `npm run test:e2e` contra API/PostgreSQL reales | **4/4**, incluidos los dos flujos anteriores, el espacio nuevo de investigación/tareas y la previsualización de un archivo técnico sin origen verificado |
| `dotnet run --no-build --project src/NotebookYt.Api -- --migrate` en base nueva `complete_studio_roundtrip`; `dotnet ef database update 0`; `dotnet ef database update` | Migración CLI, reversión completa y reaplicación correctas |
| `dotnet ef migrations has-pending-model-changes` | Sin cambios pendientes |
| FFmpeg sobre fixture técnico; importación HTTP real con `attested=false`; consulta de archivo por HTTP | Inspección positiva; checksum correcto; origen Unverified y sin canónico |
| Captura de estado, detener API, `pg_ctl restart -m fast`, nuevo proceso API y comparación HTTP | Bytes/checksum, procedencia, paquetes, tareas y eventos conservados |
| Contratos HTTP controlados de Google | Creación, fuentes y su procesamiento, refresh, sesión reanudable y verificación de publicación probados sin operaciones reales |
| `dotnet build src/NotebookYt.Api` | 0 errores y 0 avisos |
| `dotnet list ... package --vulnerable --include-transitive`; `npm audit` | Sin vulnerabilidades conocidas |
| Preview y capturas Playwright de escritorio/móvil | Inspección visual realizada; sin desbordamiento a 375 px; controles de teclado cubiertos en E2E anterior |
| `impeccable detect --json ...` | Sin incidencias detectadas |
| Python `yaml.safe_load(compose.yaml)` | YAML válido; no demuestra ejecución Docker |
| `Get-NetTCPConnection` de API, Vite y PostgreSQL | Enlaces exclusivamente en 127.0.0.1 |
| `git diff --check` y búsqueda de patrones de credenciales | Sin errores de whitespace ni secretos reales identificados |

## 14–16. Evidencia de pruebas y persistencia

41 pruebas xUnit agregan validaciones, política editorial, fuentes/paquetes/tareas, restricciones SQL, idempotencia, programación DST, recuperación, autenticación/CSRF, medios, miniaturas y contratos Google. La prueba técnica completa usa expresamente un doble de declaración del operador en una base efímera; no demuestra origen genuino de NotebookLM.

10 pruebas frontend comprueban creación/edición, filtros, conservación de borradores con error, cancelación, recuperación, módulos, capacidades manuales y login con CSRF. Cuatro E2E reales verifican los flujos editoriales y el recorrido de investigación hasta el bloqueo correcto por falta de vídeo canónico. No suben nada a Google.

Persistencia comprobada reiniciando el proceso API y PostgreSQL. El archivo sintético de comprobación permanece `Unverified`; se compararon sus bytes y SHA256 después del reinicio. Se conservaron paquetes, tareas y auditoría de una producción separada. La reversión de migraciones solo se realizó en una base nueva y dedicada, nunca sobre datos del operador.

## 17–19. Límites, credenciales y aceptación pendiente

- No hay todavía una exportación genuina autorizada de NotebookLM para ejecutar la aceptación de procedencia real. El operador explicó el recorrido de generación y descarga en la web; no se recibió un archivo exportado para esta prueba.
- No se proporcionaron cliente/secret OAuth, autorización efectiva de canal, proyecto Enterprise ni licencia verificada. No se accedió a cuentas Google ni se realizaron publicaciones.
- Docker no está disponible. Se entregan definición e instrucciones, pero la imagen/volúmenes no están verificados ejecutándose.
- No se ejecutó una restauración completa de backup ni se aceptó un despliegue remoto/HTTPS.
- La subida local de archivos es streaming e idempotente por contenido, pero no implementa reanudación por bloques desde el navegador. La subida a YouTube sí es reanudable.
- Los paquetes son deliberadamente inmutables; no hay un editor de revisiones del mismo paquete. Para contexto distinto se abre otra producción.
- No hay búsqueda automática de tendencias ni otro LLM para sugerencias. La entrada/importación de propuestas es manual y conserva su origen.
- El adaptador no cubre toda la administración de notebooks y su aceptación efectiva contra la cuenta Google sigue pendiente. No se declara que toda la superficie oficial esté implementada o verificada.

La operación siguiente es generar un Video Overview en NotebookLM, descargarlo mediante su control oficial y seleccionarlo en Importar archivo. La aplicación indica los pasos y conserva la procedencia declarada. Para operaciones automáticas oficiales, configurar OAuth y licencias sin enviar secretos por chat y aprobar pruebas reales separadamente. Ninguno de esos pasos se sustituye por generación ajena o respuestas mock.

## 20. Git

Rama: `feat/complete-notebook-yt`, basada en `6111e83`, que conserva `46fd966` y las correcciones posteriores. Entrega mediante commits descriptivos de aplicación, pruebas y operación/documentación. No se hizo merge a main ni force-push. Las referencias exactas se proporcionan en la respuesta final.
