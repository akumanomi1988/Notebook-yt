const stages: Record<string, string> = {
  Draft: 'Borrador', Research: 'Investigar fuentes', Unassigned: 'Falta tema', Uploading: 'Subiendo', Succeeded: 'Completado',
  BriefReady: 'Brief preparado', AwaitingNotebook: 'Pendiente de NotebookLM',
  NotebookPending: 'Preparar cuaderno', NotebookReady: 'Preparar fuentes', AwaitingNotebookGeneration: 'Generando en NotebookLM',
  AwaitingVideoDownload: 'Descargar vídeo', AwaitingVideoImport: 'Importar vídeo', VideoImported: 'Vídeo importado',
  QualityReview: 'Revisar vídeo', ReadyForUpload: 'Listo para subir', Uploaded: 'Subido', Published: 'Publicado',
  Blocked: 'Bloqueado', Cancelled: 'Cancelado', PendingInspection: 'Inspeccionando', Valid: 'Válido', Invalid: 'No válido',
  WaitingForTopic: 'Falta tema', WaitingForVideo: 'Falta vídeo', Planned: 'Planificado', Pending: 'Pendiente',
  Queued: 'En cola', Running: 'En ejecución', Completed: 'Completado', Failed: 'Falló', NeedsAttention: 'Requiere atención',
};
const tasks: Record<string, string> = {
  CreateNotebook: 'Crear cuaderno', AddNotebookSources: 'Incorporar fuentes', ConfigureVideoOverview: 'Configurar vídeo',
  ReviewProducerNotes: 'Revisar notas de productor', StartVideoGeneration: 'Iniciar generación',
  ConfirmVideoGeneration: 'Comprobar vídeo terminado', DownloadNotebookVideo: 'Descargar original', ImportNotebookVideo: 'Importar e inspeccionar',
};
export const stageLabel = (value: string) => stages[value] ?? value;
export const taskLabel = (value: string) => tasks[value] ?? value;
