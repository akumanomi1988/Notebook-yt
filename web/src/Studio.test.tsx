// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@testing-library/jest-dom/vitest';
import { Studio } from './Studio';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
test('studio displays actual manual capability status and registers topics without invented metrics', async () => {
  const fetchMock = vi.fn(async (url: string, init: RequestInit) => {
    let data: unknown;
    if (url === '/api/studio/settings') data = { channel: { id: 'c', name: 'Editorial', timeZone: 'Etc/UTC' }, settings: {}, niches: [{ id: 'n', name: 'History', weekday: 1, enabled: true }], nicheSettings: [] };
    else if (url === '/api/studio/topics' && init.method === 'POST') data = { id: 't' };
    else if (url === '/api/studio/topics' || url === '/api/productions') data = [];
    else if (url === '/api/studio/overview') data = { pendingTasks: [], workspaces: [], jobs: [], slots: [], uploads: [] };
    else if (url === '/api/studio/integrations') data = { oauthConfigured: false, connections: [], capabilities: [{ operation: 'VideoOverviewGeneration', documented: false, implementation: 'ManualOnly', liveVerified: false, fallback: 'OfficialNotebookInterface' }] };
    else throw new Error('Unexpected request');
    return new Response(JSON.stringify(data), { status: 200 });
  });
  vi.stubGlobal('fetch', fetchMock); const user = userEvent.setup(); render(<Studio />);
  await screen.findByText('Trabajo pendiente'); await user.click(screen.getByRole('button', { name: 'Integraciones' }));
  expect(screen.getByText('ManualOnly')).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Autorizar YouTube' })).toBeDisabled();
  await user.click(screen.getByRole('button', { name: 'Temas' }));
  await user.type(screen.getByLabelText('Título del tema'), 'Evergreen history'); await user.type(screen.getByLabelText('Evaluación evergreen'), 'Operator assessment');
  await user.click(screen.getByRole('button', { name: 'Registrar tema' }));
  await screen.findByText('Operación guardada.');
  const call = fetchMock.mock.calls.find(([url, init]) => url === '/api/studio/topics' && init.method === 'POST');
  expect(JSON.parse(call![1].body as string)).toMatchObject({ title: 'Evergreen history', origin: 'Manual', priority: 50 });
});

test('studio errors remain recoverable', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('Backend offline'))); render(<Studio />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Backend offline'); expect(screen.getByRole('button', { name: 'Actualizar datos' })).toBeEnabled();
});

test('grouped editorial settings retain hidden quality values and save daily planning', async () => {
  const settings = { language: 'es', publishingTime: '18:00', visibility: 'private', horizonDays: 14, leadDays: 1, minimumHeight: 720, minimumDurationSeconds: 60, maximumDurationSeconds: 1200, retentionDays: 90, requireEditorialApproval: true, notebookProduct: 'Personal' };
  const fetchMock = vi.fn(async (url: string, init: RequestInit) => {
    const data = url === '/api/studio/settings' ? { channel: { id: 'c', name: 'NewSphere', timeZone: 'Europe/Madrid' }, settings, niches: [], nicheSettings: [] }
      : url === '/api/studio/overview' ? { pendingTasks: [], workspaces: [], jobs: [], slots: [], uploads: [] }
      : url === '/api/studio/integrations' ? { oauthConfigured: true, connections: [], capabilities: [] } : [];
    return new Response(JSON.stringify(init.method === 'PUT' ? {} : data));
  });
  vi.stubGlobal('fetch', fetchMock);
  const user = userEvent.setup(); render(<Studio view="Ajustes" embedded />);
  await screen.findByLabelText('Idioma principal');
  expect(screen.queryByRole('navigation', { name: 'Módulos de producción' })).not.toBeInTheDocument();
  await user.click(screen.getByLabelText('Activar planificación automática'));
  await user.type(screen.getByLabelText('Descripción del canal'), 'Verified editorial context');
  await user.click(screen.getByRole('button', { name: 'Guardar configuración editorial' }));
  await screen.findByText('Operación guardada.');
  const call = fetchMock.mock.calls.find(([url, init]) => url === '/api/studio/settings' && init.method === 'PUT');
  expect(JSON.parse(call![1].body as string)).toMatchObject({ ...settings, schedulingEnabled: true, description: 'Verified editorial context' });
});
