import { test as base, expect, type BrowserContext } from '@playwright/test';

type OperatorState = Awaited<ReturnType<BrowserContext['storageState']>>;

// This profile is exclusively for the isolated local acceptance instance.
// Authenticate once through the real UI; retain the production login limiter.
export const test = base.extend<object, { operatorState: OperatorState }>({
  operatorState: [async ({ browser }, use) => {
    const context = await browser.newContext({ baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://127.0.0.1:5173' });
    const page = await context.newPage();
    await page.goto('/');
    await page.getByLabel('Contraseña', { exact: true }).fill('controlled-acceptance-password');
    await page.getByRole('button', { name: 'Entrar', exact: true }).click();
    await expect(page.getByText('Operador autenticado', { exact: true })).toBeVisible();
    expect((await (await page.request.get('/api/auth/session')).json()).required).toBe(true);
    await use(await context.storageState());
    await context.close();
  }, { scope: 'worker' }],
  storageState: async ({ operatorState }, use) => { await use(operatorState); },
  request: async ({ playwright, operatorState }, use) => {
    const request = await playwright.request.newContext({ baseURL: 'http://127.0.0.1:5080', storageState: operatorState });
    const session = await (await request.get('/api/auth/session')).json();
    expect(session.required && session.authenticated).toBe(true);
    const authenticated = await playwright.request.newContext({
      baseURL: 'http://127.0.0.1:5080', storageState: await request.storageState(),
      extraHTTPHeaders: { 'X-CSRF-TOKEN': session.csrfToken },
    });
    await request.dispose();
    await use(authenticated);
    await authenticated.dispose();
  },
});
export { expect };
