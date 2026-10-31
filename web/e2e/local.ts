import { test as base, expect, type BrowserContext } from '@playwright/test';

type LocalState = {
  storageState: Awaited<ReturnType<BrowserContext['storageState']>>;
  csrfToken: string;
};

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://127.0.0.1:5173';

export const test = base.extend<object, { localState: LocalState }>({
  localState: [async ({ playwright }, use) => {
    const bootstrap = await playwright.request.newContext({ baseURL });
    const response = await bootstrap.get('/api/security/csrf');
    expect(response.ok()).toBe(true);
    const { csrfToken } = await response.json() as { csrfToken: string };
    expect(csrfToken).toBeTruthy();
    await use({ storageState: await bootstrap.storageState(), csrfToken });
    await bootstrap.dispose();
  }, { scope: 'worker' }],
  storageState: async ({ localState }, use) => { await use(localState.storageState); },
  request: async ({ playwright, localState }, use) => {
    const request = await playwright.request.newContext({
      baseURL,
      storageState: localState.storageState,
      extraHTTPHeaders: { 'X-CSRF-TOKEN': localState.csrfToken },
    });
    await use(request);
    await request.dispose();
  },
});

export { expect };
