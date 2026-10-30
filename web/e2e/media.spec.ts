import { execFileSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { expect, test } from '@playwright/test';

test('unverified technical media can be previewed but never silently becomes canonical', async ({ page, request }, testInfo) => {
  const fixture = testInfo.outputPath('synthetic-technical-ui-only.mp4'); mkdirSync(dirname(fixture), { recursive: true });
  execFileSync(process.env.TEST_FFMPEG ?? 'ffmpeg', ['-v', 'error', '-f', 'lavfi', '-i', 'color=c=blue:s=1280x720:r=5', '-f', 'lavfi', '-i', 'anullsrc=r=44100:cl=stereo', '-t', '2', '-c:v', 'libx264', '-c:a', 'aac', '-pix_fmt', 'yuv420p', fixture], { windowsHide: true });
  const week = await (await request.get('http://127.0.0.1:5080/api/week')).json();
  const title = `UNVERIFIED technical media ${Date.now()}`;
  const created = await request.post('http://127.0.0.1:5080/api/productions', { data: { contentNicheId: week.find((n: { enabled: boolean }) => n.enabled).id, topic: title, plannedDate: '2027-01-01' } });
  expect(created.status()).toBe(201);
  await page.goto('/'); await page.getByRole('button', { name: 'Centro de producción', exact: true }).click();
  await page.getByRole('navigation', { name: 'Módulos de producción' }).getByRole('button', { name: 'Producciones', exact: true }).click();
  await page.getByRole('button', { name: title, exact: true }).click();
  await page.getByRole('button', { name: 'Vídeo y calidad', exact: true }).click();
  await page.getByLabel('Archivo local', { exact: true }).setInputFiles(fixture);
  await page.getByRole('button', { name: 'Importar archivo para inspección', exact: true }).click();
  await expect.poll(async () => {
    await page.getByRole('button', { name: 'Actualizar datos', exact: true }).click();
    return await page.getByRole('heading', { name: 'synthetic-technical-ui-only.mp4 · Valid', exact: true }).count();
  }, { timeout: 20000, intervals: [1000, 2000] }).toBe(1);
  await expect(page.getByRole('button', { name: 'Seleccionar vídeo canónico', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Extraer fotograma como miniatura', exact: true })).toBeDisabled();
  await page.waitForFunction(() => { const video = document.querySelector('video'); return video && video.videoWidth === 1280; });
  expect(await page.locator('video').evaluate(v => (v as HTMLVideoElement).duration)).toBeGreaterThan(1);
  await page.getByRole('button', { name: 'Comprobar calidad', exact: true }).click();
  await expect(page.getByText('Falta un vídeo canónico válido de NotebookLM.', { exact: true })).toBeVisible();
});
