// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { NotebookLmExperts } from './NotebookLmExperts';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it('shows that unverified account access disables real NotebookLM queries', async () => {
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    const value = url.endsWith('/diagnostics')
      ? { cliVersion: '0.8.6', distribution: 'CLI comunitaria', configured: true, authentication: { success: false, code: 'AUTH_REQUIRED' }, usageState: 'authentication_required', providerVerified: false }
      : url.endsWith('/experts') || url.endsWith('/operations') || url.endsWith('/expert-templates') ? [] : {};
    return new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } });
  }));
  render(<NotebookLmExperts productions={[]} busy={false} run={action => { void action(); }} />);
  await waitFor(() => expect(screen.getByText(/autenticación: AUTH_REQUIRED/)).toBeTruthy());
  expect(screen.getByRole('button', { name: 'Enviar consulta a NotebookLM' })).toHaveProperty('disabled', true);
  expect(screen.getByText(/no crean cuadernos automáticamente/i)).toBeTruthy();
});

it('renders linked experts while the provider diagnostic is still pending', async () => {
  let finishDiagnostic!: (response: Response) => void;
  const diagnostic = new Promise<Response>(resolve => { finishDiagnostic = resolve; });
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.endsWith('/diagnostics')) return diagnostic;
    const value = url.endsWith('/experts') ? [{ id: 'e1', name: 'Research Expert', specialization: 'research', description: 'Research', notebookId: 'notebook-1', instructions: 'Verify sources', stageKeysJson: '[]', enabled: true, version: 1, lastSyncedAtUtc: null, lastErrorCode: '' }] : [];
    return new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } });
  }));
  render(<NotebookLmExperts productions={[]} busy={false} run={action => { void action(); }} />);
  expect(await screen.findByRole('heading', { name: /Research Expert/ })).toBeTruthy();
  finishDiagnostic(new Response(JSON.stringify({ cliVersion: '0.8.6', distribution: 'CLI', configured: true, authentication: { success: true, code: 'OK' }, usageState: 'available', providerVerified: true }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
  await waitFor(() => expect(screen.getByText(/available/)).toBeTruthy());
});
