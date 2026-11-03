import { useEffect, useState } from 'react';
import { apiRequest } from './client';

type Workflow = {
  stage: string; readyForReserve: boolean; publicationApproved: boolean; uploaded: boolean;
  quality: { failures: string[]; warnings: string[] }; expertFailures: string[];
  nextAction: string; limitations: string[];
  operations: { id: string; kind: string; status: string; errorCode: string; providerTaskId: string; providerArtifactId: string }[];
};

export function ProductionWorkflow({ productionId }: { productionId: string }) {
  const [state, setState] = useState<Workflow | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  useEffect(() => {
    let active = true;
    setLoading(true); setState(null); setError('');
    apiRequest<Workflow>(`/studio/productions/${productionId}/workflow`)
      .then(value => { if (active) setState(value); })
      .catch(e => { if (active) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [productionId]);
  const refresh = async () => {
    setLoading(true); setError('');
    try { setState(await apiRequest<Workflow>(`/studio/productions/${productionId}/workflow`)); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setLoading(false); }
  };
  return <section aria-label="Flujo y reserva">
    <h4>Flujo y reserva</h4>
    <p>La web y el agente consultan este mismo estado del servidor. Las respuestas de expertos y los artefactos deben estar registrados en esta producción.</p>
    <button type="button" disabled={loading} onClick={() => void refresh()}>Actualizar estado del flujo</button>
    {loading && <p role="status">Comprobando evidencias y reserva…</p>}
    {error && <p role="alert">{error}</p>}
    {state && <>
      <p><strong>{state.uploaded ? 'Vídeo ya subido' : state.readyForReserve ? 'Paquete listo en la reserva' : 'Producción incompleta'}</strong></p>
      <p>Siguiente paso: {state.nextAction}</p>
      <ul>{[...state.quality.failures, ...state.expertFailures].map((failure, index) => <li key={index}>{failure}</li>)}</ul>
      {state.readyForReserve && <a href={`/api/studio/productions/${productionId}/reserve.zip`}>Descargar MP4, miniatura y JSON de YouTube</a>}
      <h4>Operaciones registradas</h4>
      {state.operations.length ? <ul>{state.operations.map(op => <li key={op.id}>{op.kind}: {op.kind === 'GenerateVideo' && op.status === 'Completed' ? 'Solicitud aceptada; vídeo pendiente de verificar' : op.status}{op.errorCode && ` · ${op.errorCode}`}{op.providerTaskId && ` · tarea ${op.providerTaskId}`}</li>)}</ul> : <p>No hay operaciones NotebookLM registradas en esta producción.</p>}
      <details><summary>Automatización pendiente de integrar</summary><ul>{state.limitations.map(item => <li key={item}>{item}</li>)}</ul></details>
      {state.quality.warnings.map(item => <p key={item}>{item}</p>)}
    </>}
  </section>;
}
