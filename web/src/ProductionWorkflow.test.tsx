// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@testing-library/jest-dom/vitest';
import { ProductionWorkflow } from './ProductionWorkflow';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const incomplete = { stage: 'Research', readyForReserve: false, uploaded: false, publicationApproved: false,
  quality: { failures: ['Falta vídeo original'], warnings: [] }, expertFailures: ['Falta investigador'],
  nextAction: 'Preparar fuentes', limitations: ['Infografía pendiente'], operations: [] };

test('incomplete productions cannot download a reserve package and errors recover', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new Error('Backend offline'))
    .mockResolvedValue(new Response(JSON.stringify(incomplete)));
  vi.stubGlobal('fetch', fetch);
  render(<ProductionWorkflow productionId="episode" />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Backend offline');
  await userEvent.click(screen.getByRole('button', { name: 'Actualizar estado del flujo' }));
  expect(await screen.findByText('Falta vídeo original')).toBeInTheDocument();
  expect(screen.queryByRole('link')).not.toBeInTheDocument();
});

test('only a ready server response exposes the complete reserve download', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ ...incomplete,
    readyForReserve: true, publicationApproved: true, quality: { failures: [], warnings: [] }, expertFailures: [] }))));
  render(<ProductionWorkflow productionId="episode" />);
  expect(await screen.findByRole('link')).toHaveAttribute('href', '/api/studio/productions/episode/reserve.zip');
});
