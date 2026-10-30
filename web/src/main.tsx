import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

type Channel = { name: string; timeZone: string };
type Niche = { id: string; weekday: number; name: string; description: string; guidance: string; enabled: boolean };
type Production = { id: string; contentNicheId: string; nicheNameSnapshot: string; topic: string; plannedDate: string; status: string };
type Detail = { production: Production; history: { from: string | null; to: string; changedAtUtc: string }[]; notebook: { url: string } | null };
const days = ['Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado', 'Domingo'];
async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await fetch('/api' + path, { method, headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) { const data = await response.json().catch(() => ({})); throw new Error(data.detail ?? data.error ?? JSON.stringify(data.errors ?? {}) ?? 'Request failed'); }
  return response.json() as Promise<T>;
}
function App() {
  const [tab, setTab] = useState<'week' | 'productions'>('week');
  const [channel, setChannel] = useState<Channel>({ name: 'Local channel', timeZone: 'Etc/UTC' });
  const [niches, setNiches] = useState<Niche[]>([]);
  const [productions, setProductions] = useState<Production[]>([]);
  const [selected, setSelected] = useState<Detail | null>(null);
  const [topic, setTopic] = useState('');
  const [plannedDate, setPlannedDate] = useState(new Date().toISOString().slice(0, 10));
  const [nicheId, setNicheId] = useState('');
  const [notebookUrl, setNotebookUrl] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const run = async (operation: () => Promise<void>) => { setError(''); setNotice(''); setSaving(true); try { await operation(); setNotice('Cambios guardados.'); } catch (e) { setError(String(e instanceof Error ? e.message : e)); } finally { setSaving(false); } };
  const refresh = async () => { const [c, n, p] = await Promise.all([request<Channel | null>('/channel'), request<Niche[]>('/week'), request<Production[]>('/productions')]); if (c) setChannel(c); setNiches(n); setProductions(p); if (!nicheId && n.length) setNicheId(n.find(x => x.enabled)?.id ?? ''); };
  useEffect(() => { refresh().catch(e => setError(String(e))).finally(() => setLoading(false)); }, []);
  const open = async (id: string) => { setError(''); try { const detail = await request<Detail>('/productions/' + id); setSelected(detail); setNotebookUrl(detail.notebook?.url ?? ''); } catch (e) { setError(String(e)); } };
  const saveChannel = () => run(async () => { await request('/channel', 'PUT', channel); await refresh(); });
  const saveNiche = (n: Niche) => run(async () => { await request('/week/' + n.weekday, 'PUT', n); await refresh(); });
  const createProduction = () => run(async () => { const p = await request<Production>('/productions', 'POST', { contentNicheId: nicheId, topic, plannedDate }); setTopic(''); await refresh(); await open(p.id); });
  const transition = (status: string) => selected && run(async () => { await request('/productions/' + selected.production.id + '/status', 'POST', { status }); await refresh(); await open(selected.production.id); });
  const saveNotebook = () => selected && run(async () => { await request('/productions/' + selected.production.id + '/notebook', 'PUT', { url: notebookUrl }); await open(selected.production.id); });
  const updateNiche = (id: string, key: keyof Niche, value: string | boolean) => setNiches(ns => ns.map(n => n.id === id ? { ...n, [key]: value } : n));
  if (loading) return <main><p>Cargando datos editoriales…</p></main>;
  return <main><header><h1>Notebook-yt</h1><p>Gestión editorial local · NotebookLM manual</p><nav><button onClick={() => setTab('week')}>Semana editorial</button><button onClick={() => setTab('productions')}>Producciones</button></nav></header>
    {error && <p role="alert" className="error">{error}</p>}{notice && <p role="status">{notice}</p>}
    {tab === 'week' ? <><section><h2>Canal</h2><label>Nombre<input value={channel.name} maxLength={160} onChange={e => setChannel({ ...channel, name: e.target.value })}/></label><label>Zona horaria IANA<input value={channel.timeZone} onChange={e => setChannel({ ...channel, timeZone: e.target.value })}/></label><button disabled={saving} onClick={saveChannel}>Guardar canal e inicializar semana</button></section><h2>Asignaciones semanales</h2>{!niches.length && <p>Configura el canal para generar siete asignaciones neutras.</p>}<div className="grid">{niches.map(n => <section key={n.id}><h3>{days[n.weekday - 1]}</h3><label>Nombre<input value={n.name} onChange={e => updateNiche(n.id, 'name', e.target.value)}/></label><label>Descripción<textarea value={n.description} onChange={e => updateNiche(n.id, 'description', e.target.value)}/></label><label>Guía editorial<textarea value={n.guidance} onChange={e => updateNiche(n.id, 'guidance', e.target.value)}/></label><label className="inline"><input type="checkbox" checked={n.enabled} onChange={e => updateNiche(n.id, 'enabled', e.target.checked)}/>Activo</label><button disabled={saving} onClick={() => saveNiche(n)}>Guardar {days[n.weekday - 1]}</button></section>)}</div></> :
    <><section><h2>Nueva producción</h2><label>Tema de trabajo<input value={topic} onChange={e => setTopic(e.target.value)}/></label><label>Nicho<select value={nicheId} onChange={e => setNicheId(e.target.value)}>{niches.filter(n => n.enabled).map(n => <option key={n.id} value={n.id}>{n.name}</option>)}</select></label><label>Fecha prevista<input type="date" value={plannedDate} onChange={e => setPlannedDate(e.target.value)}/></label><button disabled={saving || !topic.trim() || !nicheId} onClick={createProduction}>Crear producción</button></section><section><h2>Producciones</h2>{!productions.length ? <p>No hay producciones todavía.</p> : <ul>{productions.map(p => <li key={p.id}><button className="link" onClick={() => open(p.id)}>{p.topic} · {p.plannedDate} · {p.status}</button></li>)}</ul>}</section>
    {selected && <section><h2>{selected.production.topic}</h2><p>Nicho original: {selected.production.nicheNameSnapshot} · Estado: {selected.production.status}</p><p>Siguiente acción: {selected.production.status === 'Draft' ? 'Preparar el briefing' : selected.production.status === 'BriefReady' ? 'Preparar NotebookLM manualmente' : selected.production.status === 'AwaitingNotebook' ? 'Registrar o revisar el notebook' : selected.production.status === 'Blocked' ? 'Resolver bloqueo' : 'Ninguna: cancelada'}</p><h3>Cambiar estado</h3><div className="actions">{['Draft','BriefReady','AwaitingNotebook','Blocked','Cancelled'].filter(s => s !== selected.production.status).map(s => <button disabled={saving} key={s} onClick={() => transition(s)}>{s}</button>)}</div><h3>NotebookLM</h3><label>URL del notebook<input type="url" value={notebookUrl} onChange={e => setNotebookUrl(e.target.value)} placeholder="https://notebooklm.google.com/notebook/..."/></label><button disabled={saving || !notebookUrl.trim()} onClick={saveNotebook}>Registrar referencia</button><h3>Historial</h3><ol>{selected.history.map((h, i) => <li key={i}>{h.from ?? 'Inicio'} → {h.to} · {new Date(h.changedAtUtc).toLocaleString()}</li>)}</ol></section>}</>}
  </main>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
