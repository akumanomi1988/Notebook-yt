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
