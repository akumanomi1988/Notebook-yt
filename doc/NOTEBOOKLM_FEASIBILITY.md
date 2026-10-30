# Viabilidad de NotebookLM — revisión 2026-10-30

La documentación oficial consultada usa actualmente el nombre Gemini Notebook. Phase 1 conserva NotebookLM como referencia del producto y solo registra URLs externas.

## Capacidades documentadas

La [API Enterprise de notebooks](https://docs.cloud.google.com/gemini/enterprise/notebooklm-enterprise/docs/api-notebooks) está en Preview y documenta creación, consulta, listado de notebooks recientes, eliminación y compartición. Los enlaces Enterprise incluyen una región antes de `/notebook/<id>` y el proyecto en la consulta; la validación local acepta los hosts oficiales documentados.

La [API de fuentes](https://docs.cloud.google.com/gemini/enterprise/notebooklm-enterprise/docs/api-notebooks-sources) permite incorporación por lotes, subida de archivos, consulta y eliminación de fuentes. Incluye texto, contenido web, YouTube, Docs y Slides. El acceso a Drive exige autorización adicional.

La [configuración Enterprise](https://docs.cloud.google.com/gemini/enterprise/notebooklm-enterprise/docs/set-up-notebooklm) requiere proyecto e identidad configurados, roles IAM y licencia por usuario. La licencia personal de consumo no demuestra autorización para usar esta API Enterprise.

Existe una [API de Audio Overview](https://docs.cloud.google.com/gemini/enterprise/notebooklm-enterprise/docs/api-audio-overview). No debe confundirse con una API de Video Overview.

La [ayuda oficial de Video Overviews](https://support.google.com/notebooklm/answer/16454555?hl=en) documenta generación desde Studio y descarga mediante la interfaz. Algunas variantes y funciones necesitan suscripciones específicas; co-creación con notas de productor requiere Google AI Ultra. La descarga puede tener restricciones según las fuentes.

## Límites de la conclusión

En las páginas oficiales revisadas no se identificó una API pública soportada para automatizar notebooks de cuentas personales, generar Video Overviews o descargar sus vídeos por API. Esto es una limitación de la evidencia revisada, no una afirmación de que Google nunca pueda ofrecerla. La generación y descarga disponibles en la interfaz no prueban que exista un contrato API equivalente.

No se verificaron licencias de esta cuenta, cuotas, permisos efectivos ni acceso Enterprise. No se hicieron llamadas autenticadas, no se contrataron servicios y no se incurrió en costes. Phase 1 no usa cookies, scraping, endpoints privados ni simulaciones de generación/publicación.
