// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { CsrfBootstrap } from './CsrfBootstrap';
import { apiRequest, setCsrfToken } from './client';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); setCsrfToken(''); });

test('opens the workspace without operator login and protects writes with CSRF', async () => {
  const fetchMock = vi.fn(async (_url: string, _init?: RequestInit) =>
    new Response(JSON.stringify({ csrfToken: 'local-csrf-token' }), { status: 200 }));
  vi.stubGlobal('fetch', fetchMock);
  render(<CsrfBootstrap><p>Espacio editorial</p></CsrfBootstrap>);

  expect(await screen.findByText('Espacio editorial')).toBeInTheDocument();
  expect(screen.queryByLabelText(/contraseña/i)).not.toBeInTheDocument();
  expect(fetchMock).toHaveBeenCalledWith('/api/security/csrf', expect.objectContaining({ method: 'GET' }));

  await apiRequest('/channel', 'PUT', { name: 'NewSphere', timeZone: 'Europe/Madrid' });
  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
  expect(fetchMock.mock.calls[1][1]?.headers).toMatchObject({ 'X-CSRF-TOKEN': 'local-csrf-token' });
});
