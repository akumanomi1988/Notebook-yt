import { expect, type Page } from '@playwright/test';

export async function prepareEditorial(page: Page, sourceTitle: string) {
  await page.getByRole('button', { name: 'Dirección editorial', exact: true }).click();
  await expect(page.getByLabel('Pregunta central', { exact: true })).toBeVisible();
  await page.getByLabel('Pregunta central', { exact: true }).fill('¿Cómo se interpreta este mecanismo con evidencia contrastada?');
  await page.getByLabel('Premisa que se va a contrastar').fill('La explicación necesita contrastar el mecanismo y sus límites.');
  await page.getByLabel('Aportación original', { exact: true }).fill('Relacionar un ejemplo concreto con el mecanismo y diferenciar lo documentado de lo desconocido.');
  await page.getByRole('textbox', { name: 'Público destinatario', exact: true }).fill('Personas adultas sin conocimientos técnicos previos.');
  await page.getByRole('button', { name: 'Narrativa', exact: true }).click();
  await page.getByLabel('Apertura del vídeo').fill('Una pregunta concreta sobre el mecanismo y por qué importa.');
  await page.getByLabel('Plan de evidencias', { exact: true }).fill('Contrastar cada afirmación con las fuentes seleccionadas y registrar sus limitaciones.');
  await page.getByRole('button', { name: 'Dirección editorial', exact: true }).last().click();
  await page.getByLabel('Criterios de calidad del episodio').fill('Explicación comprensible, hechos atribuibles y visuales no engañosos.');
  await page.getByRole('button', { name: 'Fuentes y paquete', exact: true }).click();
  await page.getByRole('checkbox', { name: sourceTitle, exact: true }).check();
  await page.getByRole('button', { name: 'Guardar dirección editorial', exact: true }).click();
  await expect(page.getByText('Dirección editorial guardada.', { exact: true })).toBeVisible();
  await page.getByLabel('Justificación de diferencias o continuidad de serie').fill('Serie de pruebas controladas: mismo enfoque legítimo, ejemplos y producciones independientes.');
  await page.getByRole('button', { name: 'Guardar dirección editorial', exact: true }).click();
  await expect(page.getByText('Dirección editorial guardada.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aprobar paquete editorial', exact: true })).toBeEnabled();
}
