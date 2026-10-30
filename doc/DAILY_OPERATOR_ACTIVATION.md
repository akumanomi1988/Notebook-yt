# Activación diaria y experiencia del operador

Fecha: 2026-10-30. Rama de entrega: `feat/daily-operator-experience`.

## Resultado real

La aplicación empaquetada está instalada en el perfil persistente del operador y escucha en `http://127.0.0.1:5080`. PostgreSQL operativo sigue separado de las bases de pruebas, en loopback:55434. Se conservan autenticación, directorio multimedia, claves DPAPI/Data Protection y tokens OAuth protegidos. No se han versionado credenciales ni archivos audiovisuales.

Se observó la generación de un Video Overview en NotebookLM, su reproducción y su descarga oficial. El MP4 genuino se importó y se inspeccionó mediante FFprobe y decodificación completa. Tiene audio, 1280×720, 417,285805 segundos y 36.685.105 bytes. Se verificó SHA256 tras reiniciar la aplicación. El origen queda declarado según la operación observada, no probado criptográficamente por Google.

El mismo original se subió una sola vez al canal autorizado. YouTube devolvió el identificador remoto, confirmó el canal, el procesamiento `succeeded`, el estado `processed` y la privacidad `private`. La consulta oficial confirmó coincidencia de título, descripción, etiquetas, idioma, categoría y público infantil con los valores aprobados. La miniatura remota se descargó desde la URL devuelta por Google y se inspeccionó visualmente: conserva el fotograma y el texto preparados. La evidencia privada con el identificador y las respuestas se guarda fuera del repositorio, en el perfil del operador.

La declaración `status.containsSyntheticMedia=true` se envió tanto al crear como al actualizar el vídeo. `videos.list` no devolvió esa propiedad, incluso después de reaplicar los mismos metadatos. La aplicación ahora diferencia un campo omitido (`null`, sin confirmar) de una discrepancia (`false`). No se presenta esa declaración como verificada ni se vuelve a subir el archivo. La [documentación oficial](https://developers.google.com/youtube/data/reference/rest/v3/videos) describe el campo de declaración; una respuesta que lo omite no acredita su valor.

La revisión editorial se aprobó para la prueba privada: incluyó inspección técnica y un muestreo visual de doce escenas. No se ha afirmado una revisión factual integral de la narración ni autorizado su difusión pública.

## Configuración guardada

Un turno diario a las 18:00, Europe/Madrid, con horizonte de 14 días y antelación de un día. Hay 13 turnos futuros y siete temas nuevos asignados a producciones para investigar. La planificación reserva trabajo; no genera ni descarga vídeos automáticamente.

También se activó una tarea diaria de operación en T3 a las 18:00 Europe/Madrid, vinculada a esta conversación. Primera ejecución prevista: 2026-10-31 a las 18:00 (17:00 UTC). Comprueba duplicados del día, prepara la cola y sube en privado solo si existe un original genuino revisado; si falta, comunica el paso manual concreto. Requiere el entorno local disponible. No convierte NotebookLM personal en una API ni garantiza una subida cuando falta el original.

| Día | Nicho |
|---|---|
| Lunes | Ciencia y exploración espacial |
| Martes | Tecnología y vida digital |
| Miércoles | Historia y patrimonio |
| Jueves | Naturaleza y clima |
| Viernes | Aprendizaje y psicología cognitiva |
| Sábado | Geografía y culturas |
| Domingo | Inventos e ingeniería cotidiana |

Los siete tienen nombre, descripción, guía, público, inclusión, exclusión y enfoque. Se guardaron normas maestras sobre fuentes primarias, límites factuales, miniaturas fieles, metadatos naturales y procedencia. Los temas nuevos están aprobados para investigar, no presentados como investigaciones ya completadas. El paquete histórico del vídeo ESA conserva sus tres fuentes originales; NotebookLM procesó dos y rechazó la tercera. La descripción de YouTube cita únicamente las dos utilizadas.

Las siguientes subidas requieren originales nuevos de NotebookLM y revisión. La cuenta personal mantiene generación y descarga en la interfaz oficial. No se creó un proveedor alternativo ni se habilitó publicación pública. Las preferencias delegadas están en [OPERATOR_PREFERENCES.md](OPERATOR_PREFERENCES.md).

## Cambios implementados

- Navegación estable, inicio con cola auténtica y estados del canal, diseño adaptable a escritorio y móvil.
- Configuración dividida en identidad, cadencia, calidad/almacenamiento y cuenta; estados internos habituales traducidos.
- Tareas completadas y paquetes inmutables plegables; indicación de carga de detalle y reinicio del resultado de calidad al cambiar los datos evaluados.
- Procedencia y evaluación editorial usan entradas separadas para no reutilizar accidentalmente la misma declaración.
- Título limitado en la interfaz a 100 caracteres y descripción a 5.000; verificación oficial de metadatos después de la subida.
- Contador de bytes corregido al completar el último bloque reanudable y al conciliar una subida terminada.
- Los turnos editoriales privados/ocultos ya no crean una fecha de publicación pública incompatible. La conservación de turnos antiguos no aprobados queda auditada mediante `PrivateScheduleAligned`.
- HTML empaquetado con `Cache-Control: no-cache` para evitar que una actualización siga mostrando un índice antiguo.
- Entorno E2E configurable en otro puerto; pruebas reales contra PostgreSQL sin tocar los datos del operador.

## Comandos y evidencia ejecutada

| Operación | Comando | Resultado |
|---|---|---|
| TypeScript | `cd web; npm run typecheck` | Correcto |
| Frontend empaquetado | `npm run build` | Correcto |
| Componentes y cliente | `npm test -- --reporter=dot` | 15 pruebas correctas |
| Backend y PostgreSQL | `dotnet test tests/NotebookYt.Tests --nologo --logger 'console;verbosity=minimal'` | 59 correctas, 0 omitidas; `NOTEBOOK_TEST_POSTGRES` apuntó al PostgreSQL aislado en 55435 |
| Aceptación aislada | `scripts/Start-LocalAcceptance.ps1 -PgBin <local> -FfmpegBin <local> -Dotnet <local> -PostgresPort 55435 -HttpPort 5082` | Compilación, publicación, migraciones y arranque correctos |
| Navegador contra API real | `PLAYWRIGHT_BASE_URL=http://127.0.0.1:5082 npm run test:e2e` | Cinco recorridos: editorial, desactivación/recuperación, medios no verificados, UX diaria y estudio |
| Regresión de UX | `npx playwright test operator-experience.spec.ts` | Correcta; escritorio 1440px, ancho 1005px, móvil 390px y controles mediante teclado |
| Detector visual | `impeccable detect --json web/src/App.tsx web/src/Studio.tsx web/src/AuthGate.tsx web/src/style.css` | `[]` |
| Publicación nativa | `dotnet publish src/NotebookYt.Api -c Release -o .audit/operator-daily-release` | Correcta; archivos empaquetados instalados en el perfil del operador |
| Copia real | `Backup-NotebookYt.ps1` invocado por el actualizador privado con `ApplicationStopped` | Dump PostgreSQL, medios, claves y ACL conservados; configuración DPAPI y binarios previos guardados por separado |
| Restauración real | `Restore-NotebookYt.ps1` a una base nueva `notebook_operator_restore_<id>` en 55435, seguido del verificador local | 9 producciones, 7 nichos, 3 fuentes, 1 paquete, 8 tareas, 3 medios, 9 estados, 59 eventos y 1 subida; hashes correctos y credenciales protegidas descifradas sin imprimirlas |
| Reinicio y salud | Lanzador persistente del operador y `GET /health` | Correctos; sesión, medios, historial y conexión preservados |

Tras el último reinicio se observó una sesión autenticada, cero conflictos entre turnos privados y fechas públicas, 36.685.105 bytes confirmados, el mismo identificador remoto y la misma huella del original. El reproductor de la aplicación cargó 1280×720, 417,285805 segundos, avanzó más de doce segundos sin error y quedó pausado. El índice HTML devolvió `Cache-Control: no-cache`. Se conserva el acceso directo existente y se corrigió el lanzador para abrir el navegador también cuando el servicio ya estaba iniciado.

Los ensayos detectaron errores iniciales en fixtures nuevos (tamaño nulo de un artefacto y fechas de teorías que colisionaban) y un selector no único al repetir el E2E. Se corrigieron esos fixtures. La suite backend final pasó completa. Las pruebas de Google con HTTP controlado siguen diferenciadas de la subida real autorizada.

La revisión visual independiente pidió adelantar los bloqueos de configuración en móvil; se añadió un resumen antes de la cola y Playwright comprueba que queda dentro del primer viewport. La comprobación posterior emitió `disposition: ship` para ese arreglo. Las capturas son del entorno de pruebas, no del perfil operativo.

## Límites pendientes

- La respuesta de YouTube omite la declaración de IA: no se afirma su confirmación remota.
- La revisión factual integral del contenido precede a una eventual publicación pública.
- No hay originales disponibles para los siguientes días. La cadencia no elimina las operaciones oficiales manuales obligatorias de NotebookLM personal.
- Docker, licencia Enterprise y generación de vídeo por API no se han verificado ni se requieren para el flujo nativo utilizado.
- No se ha comprobado en esta sesión un ciclo completo de expiración y renovación real del token; sus contratos automatizados conservan cobertura controlada.

**El recorrido real de generación, importación y subida privada funciona. La automatización diaria completa continúa limitada por la generación/descarga oficial manual y la disponibilidad de originales revisados.**
