// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@testing-library/jest-dom/vitest';
import { AuthGate } from './AuthGate';
import { setCsrfToken } from './client';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); setCsrfToken(''); });
test('private workspace requires login and sends csrf token', async () => {
  let authenticated = false;
  const fetchMock = vi.fn(async (url: string, init: RequestInit) => {
    if (url === '/api/auth/login') { expect((init.headers as Record<string,string>)['X-CSRF-TOKEN']).toBe('controlled-csrf-token'); authenticated = true; }
    return new Response(JSON.stringify(url === '/api/auth/session' ? { required: true, authenticated, configured: true, csrfToken: 'controlled-csrf-token' } : { authenticated }), { status: 200 });
  });
  vi.stubGlobal('fetch', fetchMock); const user = userEvent.setup(); render(<AuthGate><p>Private editorial data</p></AuthGate>);
  await screen.findByLabelText('Contraseña'); expect(screen.queryByText('Private editorial data')).not.toBeInTheDocument();
  await user.type(screen.getByLabelText('Contraseña'), 'controlled-password'); await user.click(screen.getByRole('button', { name: 'Entrar' }));
  expect(await screen.findByText('Private editorial data')).toBeInTheDocument();
});
