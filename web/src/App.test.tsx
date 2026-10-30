// @vitest-environment jsdom
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@testing-library/jest-dom/vitest';
import { App } from './App';

const niche = { id: 'n1', weekday: 1, name: 'History', description: '', guidance: '', enabled: true };
const production = { id: 'p1', contentNicheId: 'n1', nicheNameSnapshot: 'History', topic: 'Saved topic', plannedDate: '2026-10-30', status: 'Draft', modifiedAtUtc: '2026-10-30T10:00:00Z', nextManualAction: 'PrepareBrief' };
let fetchMock: ReturnType<typeof vi.fn>;
let items: typeof production[];
let detail: { production: typeof production; history: unknown[]; notebook: { url: string } | null };
beforeEach(() => {
  items = [];
  detail = { production: { ...production }, history: [{ from: null, to: 'Draft', changedAtUtc: '2026-10-30T10:00:00Z' }], notebook: null };
  fetchMock = vi.fn(async (url: string, init: RequestInit) => {
    const method = init.method ?? 'GET';
    let data: unknown;
    if (url === '/api/channel') data = { name: 'Test channel', timeZone: 'Europe/Madrid' };
    else if (url === '/api/week') data = [niche, { ...niche, id: 'n2', weekday: 2, name: 'Disabled', enabled: false }];
    else if (url === '/api/productions' && method === 'POST') { items = [detail.production]; data = detail.production; }
    else if (url === '/api/productions') data = items;
    else if (url === '/api/productions/p1' && method === 'PUT') { detail.production = { ...detail.production, ...JSON.parse(init.body as string), modifiedAtUtc: '2026-10-30T11:00:00Z' }; items = [detail.production]; data = detail.production; }
    else if (url === '/api/productions/p1') data = detail;
    else throw new Error('Unexpected request ' + url);
    return new Response(JSON.stringify(data), { status: 200, headers: { 'Content-Type': 'application/json' } });
  });
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

test('creates and edits a production, filters list and excludes disabled niches', async () => {
  const user = userEvent.setup(); render(<App />);
  await screen.findByText('Calendario semanal');
  await user.click(screen.getByRole('button', { name: 'Producciones' }));
  expect(within(screen.getByLabelText('Nicho para nueva producción')).queryByRole('option', { name: 'Disabled' })).not.toBeInTheDocument();
  await user.type(screen.getByLabelText('Tema de trabajo'), 'Saved topic');
  await user.click(screen.getByRole('button', { name: 'Crear producción' }));
  await screen.findByRole('heading', { name: 'Detalle de producción' });
  expect(screen.getByText('Siguiente acción: Preparar el briefing')).toBeInTheDocument();
  await user.clear(screen.getByLabelText('Tema de la producción'));
  await user.type(screen.getByLabelText('Tema de la producción'), 'Edited topic');
  await user.click(screen.getByRole('button', { name: 'Guardar producción' }));
  await screen.findByRole('button', { name: 'Edited topic' });
  expect(screen.getByLabelText('Tema de la producción')).toHaveValue('Edited topic');
  await user.selectOptions(screen.getByLabelText('Filtrar por estado'), 'Cancelled');
  expect(screen.getByText('No hay producciones para estos filtros.')).toBeInTheDocument();
});

test('preserves edit input after failed save and shows a recoverable error', async () => {
  items = [production]; const user = userEvent.setup(); render(<App />);
  await screen.findByText('Calendario semanal');
  await user.click(screen.getByRole('button', { name: 'Producciones' }));
  await user.click(screen.getByRole('button', { name: 'Saved topic' }));
  await screen.findByLabelText('Tema de la producción');
  await user.clear(screen.getByLabelText('Tema de la producción'));
  await user.type(screen.getByLabelText('Tema de la producción'), 'Retained edit');
  fetchMock.mockImplementationOnce(async () => new Response(JSON.stringify({ detail: 'Temporary database failure' }), { status: 503 }));
  await user.click(screen.getByRole('button', { name: 'Guardar producción' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Temporary database failure');
  expect(screen.getByLabelText('Tema de la producción')).toHaveValue('Retained edit');
  expect(screen.getByRole('button', { name: 'Guardar producción' })).toBeEnabled();
});

test('cancelled details cannot be edited or transitioned', async () => {
  detail.production = { ...production, status: 'Cancelled', nextManualAction: 'None' }; items = [detail.production];
  const user = userEvent.setup(); render(<App />); await screen.findByText('Calendario semanal');
  await user.click(screen.getByRole('button', { name: 'Producciones' })); await user.click(screen.getByRole('button', { name: 'Saved topic' }));
  expect(await screen.findByLabelText('Tema de la producción')).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Guardar referencia' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'BriefReady' })).not.toBeInTheDocument();
});

test('initial loading and network failure allow retry', async () => {
  fetchMock.mockRejectedValueOnce(new Error('Offline'));
  const user = userEvent.setup(); render(<App />);
  expect(screen.getByRole('status')).toHaveTextContent('Cargando');
  expect(await screen.findByRole('alert')).toHaveTextContent('Offline');
  await user.click(screen.getByRole('button', { name: 'Reintentar carga' }));
  await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  expect(screen.getByLabelText('Nombre del canal')).toHaveValue('Test channel');
});
