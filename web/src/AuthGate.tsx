import { useEffect, useState, type ReactNode } from 'react';
import { apiRequest, setCsrfToken } from './client';

type Session = { required: boolean; authenticated: boolean; configured: boolean; csrfToken: string };
export function AuthGate({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null); const [error, setError] = useState(''); const [password, setPassword] = useState(''); const [busy, setBusy] = useState(false);
  const load = async () => { const value = await apiRequest<Session>('/auth/session'); setCsrfToken(value.csrfToken); setSession(value); };
  useEffect(() => { void load().catch(e => setError(e.message)); }, []);
  if (session && (!session.required || session.authenticated)) return <>{session.required && <div className="session-bar"><span>Operador autenticado</span><button onClick={() => { void apiRequest('/auth/logout', 'POST').then(load).catch(e => setError(e.message)); }}>Cerrar sesión</button></div>}{error && <p role="alert">{error}</p>}{children}</>;
  return <main><h1>Notebook-yt</h1><section><h2>Acceso privado</h2>{error && <p role="alert">{error}</p>}{!session ? <p role="status">Conectando con el servidor…</p> : !session.configured ? <p>Falta configurar la contraseña del operador en el servidor. Consulta las instrucciones de instalación.</p> :
    <form onSubmit={e => { e.preventDefault(); setBusy(true); setError(''); void apiRequest('/auth/login', 'POST', { password }).then(async () => { setPassword(''); await load(); }).catch(e => setError(e.message)).finally(() => setBusy(false)); }}>
      <label>Contraseña<input type="password" autoComplete="current-password" required maxLength={512} value={password} onChange={e => setPassword(e.target.value)} /></label><button disabled={busy}>{busy ? 'Iniciando sesión…' : 'Entrar'}</button>
    </form>}<button disabled={busy} onClick={() => { setError(''); void load().catch(e => setError(e.message)); }}>Reintentar conexión</button></section></main>;
}
