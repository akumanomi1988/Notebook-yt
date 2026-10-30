let csrfToken = '';
export function setCsrfToken(value: string) { csrfToken = value; }
async function readResponse<T>(response: Response): Promise<T> {
  const text = await response.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; }
  catch {
    if (response.ok) throw new Error('El servidor devolvió una respuesta inesperada. Reintenta la operación.');
  }
  if (!response.ok) throw new Error(data?.errors ? Object.values(data.errors).flat().join(' ') : data?.detail ?? `Error HTTP ${response.status}. Reintenta la operación.`);
  return data as T;
}
export async function apiRequest<T>(path: string, method = 'GET', body?: unknown, extraHeaders: Record<string, string> = {}): Promise<T> {
  const response = await fetch('/api' + path, { method, credentials: 'same-origin', headers: { 'Content-Type': 'application/json', ...(csrfToken ? { 'X-CSRF-TOKEN': csrfToken } : {}), ...extraHeaders }, body: body === undefined ? undefined : JSON.stringify(body) });
  return readResponse<T>(response);
}
export async function uploadArtifact(productionId: string, kind: string, file: File, evidence: string, attested: boolean) {
  const query = new URLSearchParams({ kind, name: file.name, evidence, attested: String(attested) });
  const response = await fetch(`/api/studio/productions/${productionId}/artifacts?${query}`, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': file.type || (kind === 'Video' ? 'video/mp4' : 'application/octet-stream'), ...(csrfToken ? { 'X-CSRF-TOKEN': csrfToken } : {}) }, body: file });
  return readResponse(response);
}
