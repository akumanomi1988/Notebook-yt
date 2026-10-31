import { useEffect, useState, type ReactNode } from 'react';
import { apiRequest, setCsrfToken } from './client';

type CsrfResponse = { csrfToken: string };

export function CsrfBootstrap({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [error, setError] = useState('');

  const load = async () => {
    setError('');
    try {
      const response = await apiRequest<CsrfResponse>('/security/csrf');
      if (!response.csrfToken) throw new Error('No se pudo preparar la protección de solicitudes.');
      setCsrfToken(response.csrfToken);
      setReady(true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'No se pudo conectar con Notebook-yt.');
    }
  };

  useEffect(() => { void load(); }, []);

  if (ready) return <>{children}</>;
  return <main className="connection-page">
    <h1>Notebook-yt</h1>
    {error ? <><p role="alert">{error}</p><button onClick={() => void load()}>Reintentar conexión</button></> :
      <p role="status">Conectando con el espacio editorial…</p>}
  </main>;
}
