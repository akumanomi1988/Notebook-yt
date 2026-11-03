import { expect, test } from './local';

test('web and agent API enforce the same reserve and publication gates', async ({ page, request }) => {
  await request.put('/api/channel', { data: { name: 'Workflow acceptance', timeZone: 'Europe/Madrid' } });
  const week = await (await request.get('/api/week')).json();
  const title = `Reserva controlada ${Date.now()}`;
  const created = await request.post('/api/productions', { data: { contentNicheId: week[0].id, topic: title, plannedDate: '2027-02-01' } });
  expect(created.ok()).toBe(true);
  const production = await created.json();
  await request.post(`/api/studio/productions/${production.id}/open`);
  const workflow = await (await request.get(`/api/studio/productions/${production.id}/workflow`)).json();
  expect(workflow.readyForReserve).toBe(false);
  expect(workflow.expertFailures).toHaveLength(7);
  expect((await request.get(`/api/studio/productions/${production.id}/reserve.zip`)).status()).toBe(409);
  expect((await request.post(`/api/studio/productions/${production.id}/upload`, { data: { approved: true, evidence: 'Controlled API bypass attempt' } })).status()).toBe(409);
  await page.goto('/');
  await page.getByRole('button', { name: new RegExp(title) }).click();
  await page.getByText('Consultar flujo completo y paquete de reserva', { exact: true }).click();
  const panel = page.getByRole('region', { name: 'Flujo y reserva' });
  await expect(panel.getByText('Producción incompleta', { exact: true })).toBeVisible();
  await expect(panel.getByText('Falta una respuesta aprobada y trazable del experto: research.', { exact: true })).toBeVisible();
  await expect(panel.getByRole('link')).toHaveCount(0);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
