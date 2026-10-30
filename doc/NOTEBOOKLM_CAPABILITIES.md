# Capacidades oficiales de NotebookLM y Google

Revisión documental: 2026-10-30. La documentación consultada usa actualmente Gemini Notebook. La cuenta concreta, su licencia y su cuota restante no se verificaron en vivo.

| Operación | API oficial y estado | Licencia / autenticación | Implementación | Prueba local | Acceso de esta cuenta | Procedimiento manual |
|---|---|---|---|---|---|---|
| Crear notebook | DOCUMENTED, Preview Enterprise | Licencia Enterprise, IAM y OAuth de Google; proyecto y región | IMPLEMENTED: POST oficial y referencia persistente | LOCALLY TESTED mediante contrato HTTP controlado; idempotencia durable y resultado ambiguo detenido | UNVERIFIED | Crear notebook privado y registrar URL |
| Consultar notebook | DOCUMENTED | Mismos requisitos | IMPLEMENTED: GET oficial, marca temporal de verificación | LOCALLY TESTED dentro del contrato de fuentes | UNVERIFIED | Abrir notebook y declarar comprobación |
| Registrar notas, web y YouTube | DOCUMENTED: batchCreate | Enterprise y OAuth | IMPLEMENTED; guarda ID antes de consultar procesamiento | LOCALLY TESTED para texto, recuperación sin duplicar y procesamiento pendiente/completo | UNVERIFIED | Incorporar paquete y fuentes desde la interfaz |
| Subir documentos como fuentes | DOCUMENTED: uploadFile | Enterprise y OAuth; permisos del documento | IMPLEMENTED: cuerpo binario y cabeceras oficiales | LOCALLY TESTED mediante contrato binario controlado | UNVERIFIED | Descargar el documento local e incorporarlo en NotebookLM |
| Recuperar estado de fuente | DOCUMENTED | Enterprise y OAuth | IMPLEMENTED: GET sobre ID oficial | LOCALLY TESTED; no se recrea una fuente ya registrada | UNVERIFIED | Revisar procesamiento en NotebookLM |
| Listar, eliminar y compartir notebooks; eliminar fuentes | DOCUMENTED en la documentación Enterprise | Licencia y permisos correspondientes | No expuesto por esta aplicación: no necesario para su recorrido; no se modifica ni hace público un notebook automáticamente | No ejecutado | UNVERIFIED | Gestión explícita en la interfaz oficial |
| Generar Video Overview | API pública utilizable no verificada | Depende del producto y funciones reales de la cuenta | MANUAL ONLY: tareas secuenciales e instrucciones | LOCALLY TESTED para tareas; no generación real | UNVERIFIED | Generar en Studio de NotebookLM |
| Consultar progreso o listar vídeos | No se verificó API pública soportada | UNVERIFIED | MANUAL ONLY; no se simula progreso | Declaración del operador y exigencia del archivo, probadas | UNVERIFIED | Observar el resultado real en NotebookLM |
| Descargar vídeo | Interfaz oficial documentada; API de descarga no verificada | Permisos sobre notebook y sus fuentes | MANUAL ONLY: Download oficial e importación local | Importación e inspección técnicas probadas; exportación genuina pendiente | UNVERIFIED | Download → archivo local → Importar |
| Autorizar YouTube y renovar token | DOCUMENTED: OAuth web server | Cliente OAuth autorizado y consentimiento del dueño del canal | IMPLEMENTED: state, PKCE, tokens protegidos, renovación y selección de canal | LOCALLY TESTED para CSRF/state negativo, sesión y contrato de renovación | UNVERIFIED | Configurar cliente y conceder autorización |
| Subir vídeo | DOCUMENTED: subida reanudable | OAuth y cuota/permisos del proyecto y canal | IMPLEMENTED: sesión persistente cifrada, consulta de rango, bloques de 8 MiB, conciliación | LOCALLY TESTED con respuestas controladas, sin subida real | UNVERIFIED | Conectar cuenta y aprobar una producción válida |
| Miniatura, metadatos, programación | DOCUMENTED | OAuth, permisos y restricciones del canal | IMPLEMENTED: subida privada inicial, miniatura y actualización oficial; revisiones auditadas | LOCALLY TESTED: miniatura, metadatos y horario privado futuro con respuestas controladas | UNVERIFIED | Revisar y aprobar cambios; resolver restricciones de Google |
| Confirmar publicación | DOCUMENTED: videos.list | OAuth de canal | IMPLEMENTED: exige canal correcto y estado procesado/público | LOCALLY TESTED; intención local no produce Published | UNVERIFIED | Comprobar estado oficial |

No hay ninguna capacidad declarada LIVE VERIFIED en esta entrega. El panel puede mostrar verificación posterior únicamente cuando existan respuestas oficiales persistidas en la base en uso. Las bases efímeras de contratos controlados no son la base del operador.

## Fuentes oficiales consultadas

- [Notebooks Enterprise](https://docs.cloud.google.com/gemini/enterprise/notebooklm-enterprise/docs/api-notebooks): contratos, regiones y URLs de notebooks.
- [Fuentes Enterprise](https://docs.cloud.google.com/gemini/enterprise/notebooklm-enterprise/docs/api-notebooks-sources): texto, web, YouTube, documentos y consulta de fuentes.
- [Configuración y licencias](https://docs.cloud.google.com/gemini/enterprise/notebooklm-enterprise/docs/set-up-notebooklm): licencia, identidades y roles IAM. El adaptador implementado usa identidad Google; no implementa Workforce Identity Federation.
- [Video Overviews](https://support.google.com/gemininotebook/answer/16454555): las operaciones de vídeo se realizan en la interfaz oficial.
- [Planes y límites](https://support.google.com/gemininotebook/answer/16213268): los límites varían por producto; la documentación no demuestra la cuota restante de una cuenta.
- [OAuth](https://developers.google.com/identity/protocols/oauth2/web-server), [subida reanudable](https://developers.google.com/youtube/data/guides/using_resumable_upload_protocol), [videos.insert](https://developers.google.com/youtube/data/reference/rest/v3/videos/insert), [recurso videos](https://developers.google.com/youtube/data/reference/rest/v3/videos), [thumbnails.set](https://developers.google.com/youtube/data/reference/rest/v3/thumbnails/set) y [cuotas y auditorías](https://developers.google.com/youtube/data/guides/quota_and_compliance_audits).

La documentación de YouTube consultada publica asignaciones por tipo de llamada y cuotas del resto de operaciones. La aplicación muestra los fallos reales y aplica reintentos acotados, pero no inventa consumo ni saldo. Las condiciones efectivas deben comprobarse en el proyecto autorizado. No se activó facturación, compró una licencia, realizó una auditoría de Google ni modificó cuentas externas.

NotebookLM permanece como único origen audiovisual permitido. No se usan Veo, Gemini independiente, otros modelos, TTS ni montaje generativo alternativo. El uso de FFmpeg en la aplicación se limita a inspección y procesamiento técnico de archivos importados; los fixtures sintéticos están restringidos a las pruebas y nunca se presentan como exportaciones genuinas.
