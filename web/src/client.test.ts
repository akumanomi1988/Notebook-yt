import { afterEach, expect, it, vi } from 'vitest';
import { apiRequest, uploadArtifact } from './client';

afterEach(() => vi.unstubAllGlobals());

it('reports an HTML gateway failure and permits a successful retry', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response('<html>Gateway failure</html>', { status: 502 }))
    .mockResolvedValueOnce(new Response('{"saved":true}')));
  await expect(apiRequest('/channel')).rejects.toThrow('Error HTTP 502');
  await expect(apiRequest('/channel')).resolves.toEqual({ saved: true });
});

it('reports a non-JSON upload failure without exposing the response body', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('upstream diagnostic', { status: 413 })));
  await expect(uploadArtifact('id', 'Video', new File(['test'], 'test.mp4'), '', false)).rejects.toThrow('Error HTTP 413');
});

it('rejects an unexpected successful payload with a recoverable message', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>Unexpected</html>')));
  await expect(apiRequest('/channel')).rejects.toThrow('El servidor devolvió una respuesta inesperada');
});

it('preserves server validation details', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"errors":{"topic":["Required topic"]}}', { status: 400 })));
  await expect(apiRequest('/productions', 'POST', {})).rejects.toThrow('Required topic');
});
