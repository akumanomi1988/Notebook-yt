// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@testing-library/jest-dom/vitest';
import { EditorialDirector } from './EditorialDirector';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const draft = () => ({ fields: {}, sourceIds: [], outputEdits: {}, similarityAcknowledgement: '', similarityFingerprint: '', recommendationDecision: 'Pending', recommendationRationale: '', producerNotesAvailability: 'Unverified', producerNotesEvidence: '', cinematicAvailability: 'Unverified', cinematicEvidence: '', stylesAvailability: 'Unverified', stylesEvidence: '', requestProducerNotes: false, producerNotes: '', reviewedProducerNotes: '', revisionFeedback: '', producerNotesApproved: false });
function setup(failSave = false) {
  const effective = { objective: 'Explicar las órbitas', centralQuestion: '¿Por qué no caen los satélites?', format: 'EXPLAINER', language: 'es-ES', visualStyle: 'AUTOMATIC', archetype: 'EXPLAINER' };
  const preview = { version: 1, draft: draft(), effective, inherited: effective, origins: { objective: 'Channel', centralQuestion: 'Subniche' }, archetypes: [], sources: [], related: [], similarityFingerprint: 'fingerprint', outputs: { research: 'Plan contrastado', document: 'Documento editorial', steering: 'Enfoque', operator: 'Pasos manuales' }, failures: ['Selecciona fuentes'], warnings: [], previewHash: 'hash', frozen: false };
  const catalog = { archetypes: [{ id: 'EXPLAINER', name: 'Explicación', structure: 'Mecanismo y ejemplo' }], styles: ['AUTOMATIC', 'CUSTOM'], languages: { 'es-ES': 'Español (España)', en: 'Inglés' }, reviewCriteria: [], capabilities: [] };
  const fetchMock = vi.fn(async (url: string, init: RequestInit) => {
    if (init.method === 'PUT') return new Response(JSON.stringify(failSave ? { detail: 'Otra sesión modificó el episodio. Recarga.' } : { ...preview, version: 2, draft: JSON.parse(init.body as string).draft }), { status: failSave ? 409 : 200 });
    return new Response(JSON.stringify(url.endsWith('/catalog') ? catalog : preview));
  });
  vi.stubGlobal('fetch', fetchMock);
  render(<EditorialDirector productionId="p" artifactId={null} sources={[{ id: 'source-1', title: 'Documento ESA', included: true, provenance: 'ESA' }]} disabled={false} onSaved={async () => {}} />);
  return fetchMock;
}
test('Spanish editor shows inheritance and persists explicit overrides', async () => {
  const fetchMock = setup(); const user = userEvent.setup();
  const field = await screen.findByLabelText('Pregunta central');
  await user.clear(field); await user.type(field, '¿Cómo se mantiene un satélite en órbita?');
  await user.click(screen.getByRole('button', { name: 'Guardar dirección editorial' }));
  await screen.findByText('Dirección editorial guardada.');
  const save = fetchMock.mock.calls.find(([, init]) => init.method === 'PUT');
  expect(JSON.parse(save![1].body as string).draft.fields.centralQuestion).toBe('¿Cómo se mantiene un satélite en órbita?');
});
test('failed concurrent save retains the operator draft', async () => {
  setup(true); const user = userEvent.setup(); const field = await screen.findByLabelText('Pregunta central');
  await user.clear(field); await user.type(field, 'Mi pregunta no debe perderse');
  await user.click(screen.getByRole('button', { name: 'Guardar dirección editorial' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Otra sesión');
  expect(field).toHaveValue('Mi pregunta no debe perderse');
});
test('Cinematic Spanish incompatibility is explained immediately and blocks save', async () => {
  setup(); const user = userEvent.setup(); await screen.findByLabelText('Pregunta central');
  await user.click(screen.getByRole('button', { name: 'Configuración NotebookLM' }));
  await user.selectOptions(screen.getByLabelText('Formato de Video Overview'), 'CINEMATIC');
  expect(screen.getByRole('alert')).toHaveTextContent('Cinematic requiere inglés');
  expect(screen.getByRole('button', { name: 'Guardar dirección editorial' })).toBeDisabled();
});
test('selected sources persist without claiming remote source application', async () => {
  const fetchMock = setup(); const user = userEvent.setup(); await screen.findByLabelText('Pregunta central');
  await user.click(screen.getByRole('button', { name: 'Fuentes y paquete' }));
  expect(screen.getByText(/Esta selección local no se aplica remotamente/)).toBeInTheDocument();
  await user.click(screen.getByLabelText('Documento ESA'));
  await user.click(screen.getByRole('button', { name: 'Guardar dirección editorial' }));
  await screen.findByText('Dirección editorial guardada.');
  const save = fetchMock.mock.calls.find(([, init]) => init.method === 'PUT');
  expect(JSON.parse(save![1].body as string).draft.sourceIds).toEqual(['source-1']);
  expect(screen.getByRole('button', { name: 'Aprobar paquete editorial' })).toBeDisabled();
});
