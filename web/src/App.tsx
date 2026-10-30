import { useEffect, useRef, useState, type FormEvent } from 'react';
import { transitions } from './workflow';
import { apiRequest as request } from './client';
import { Studio } from './Studio';

type Channel = { name: string; timeZone: string };
type Niche = { id: string; weekday: number; name: string; description: string; guidance: string; enabled: boolean };
type Production = { id: string; contentNicheId: string; nicheNameSnapshot: string; topic: string; plannedDate: string; status: string; modifiedAtUtc: string; nextManualAction: string };
type Detail = { production: Production; history: { from: string | null; to: string; changedAtUtc: string }[]; notebook: { url: string } | null };
const days = ['Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado', 'Domingo'];
const actions: Record<string, string> = { PrepareBrief: 'Preparar el briefing', PrepareNotebook: 'Preparar NotebookLM manualmente', ReviewNotebook: 'Registrar o revisar el notebook', ResolveBlock: 'Resolver bloqueo', None: 'Ninguna: cancelada' };

type SaveOperation = (operation: () => Promise<void>) => Promise<void>;
function NicheEditor({ niche, saving, save }: { niche: Niche; saving: boolean; save: (n: Niche) => void }) {
  const [draft, setDraft] = useState(niche);
  return <form onSubmit={e => { e.preventDefault(); save(draft); }} aria-label={`Configuración ${days[niche.weekday - 1]}`}>
    <h3>{days[niche.weekday - 1]} <span className="badge">{niche.enabled ? 'Activo' : 'Desactivado'}</span></h3>
    <label>Nombre del nicho<input required maxLength={160} value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} /></label>
    <label>Descripción<textarea maxLength={2000} value={draft.description} onChange={e => setDraft({ ...draft, description: e.target.value })} /></label>
    <label>Guía editorial<textarea maxLength={4000} value={draft.guidance} onChange={e => setDraft({ ...draft, guidance: e.target.value })} /></label>
    <label className="inline"><input type="checkbox" checked={draft.enabled} onChange={e => setDraft({ ...draft, enabled: e.target.checked })} />Habilitar para nuevas producciones</label>
    <button disabled={saving || !draft.name.trim()}>Guardar {days[niche.weekday - 1]}</button>
  </form>;
}

function ProductionEditor({ detail, saving, run, reload }: { detail: Detail; saving: boolean; run: SaveOperation; reload: (id: string) => Promise<void> }) {
  const p = detail.production;
  const [topic, setTopic] = useState(p.topic);
  const [date, setDate] = useState(p.plannedDate);
  const [url, setUrl] = useState(detail.notebook?.url ?? '');
  const cancelled = p.status === 'Cancelled';
  const mutate = (suffix: string, method: string, body: unknown) => run(async () => { await request(`/productions/${p.id}${suffix}`, method, body); await reload(p.id); });
  return <section aria-labelledby="detail-title"><h2 id="detail-title">Detalle de producción</h2>
    <p>Nicho original: <strong>{p.nicheNameSnapshot}</strong> · Estado: <strong>{p.status}</strong></p>
    <p>Siguiente acción: {actions[p.nextManualAction] ?? 'Revisar producción'}</p>
    <form onSubmit={e => { e.preventDefault(); void mutate('', 'PUT', { topic, plannedDate: date }); }}>
      <fieldset disabled={saving || cancelled}><legend>Datos editoriales</legend>
        <label>Tema de la producción<input required maxLength={300} value={topic} onChange={e => setTopic(e.target.value)} /></label>
        <label>Fecha de publicación<input required type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
        <button disabled={!topic.trim()}>Guardar producción</button>
      </fieldset>
    </form>
    {cancelled && <p>La producción cancelada conserva sus datos y no admite cambios.</p>}
    <h3>Cambiar estado</h3><div className="actions">{(transitions[p.status] ?? []).map(status => <button key={status} disabled={saving} onClick={() => void mutate('/status', 'POST', { status })}>{status}</button>)}</div>
    <h3>Referencia de NotebookLM</h3>
    {detail.notebook && <p><a href={detail.notebook.url} target="_blank" rel="noopener noreferrer">Abrir notebook registrado</a></p>}
    <form onSubmit={e => { e.preventDefault(); void mutate('/notebook', 'PUT', { url }); }}>
      <fieldset disabled={saving || cancelled}><legend>Registro manual</legend><label>URL del notebook<input required type="url" maxLength={2048} value={url} onChange={e => setUrl(e.target.value)} placeholder="https://notebooklm.google.com/notebook/..." /></label><button>Guardar referencia</button></fieldset>
    </form>
    <h3>Historial de estados</h3><ol>{detail.history.map((h, i) => <li key={i}>{h.from ?? 'Inicio'} → {h.to} · <time dateTime={h.changedAtUtc}>{new Date(h.changedAtUtc).toLocaleString()}</time></li>)}</ol>
    <p className="secondary">Última modificación: <time dateTime={p.modifiedAtUtc}>{new Date(p.modifiedAtUtc).toLocaleString()}</time></p>
  </section>;
}

export function App({ initialTab = 'studio' }: { initialTab?: 'week' | 'productions' | 'studio' } = {}) {
  const [tab, setTab] = useState<'week' | 'productions' | 'studio'>(initialTab);
  const [studioView, setStudioView] = useState('Resumen');
  const [channel, setChannel] = useState<Channel>({ name: '', timeZone: 'Etc/UTC' });
  const [niches, setNiches] = useState<Niche[]>([]);
  const [productions, setProductions] = useState<Production[]>([]);
  const [selected, setSelected] = useState<Detail | null>(null);
  const [topic, setTopic] = useState('');
  const [plannedDate, setPlannedDate] = useState('');
  const [nicheId, setNicheId] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [nicheFilter, setNicheFilter] = useState('');
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const busy = useRef(false);
  const creationRequest = useRef({ body: '', key: '' });
  const selectionVersion = useRef(0);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const refresh = async () => {
    const [c, n, p] = await Promise.all([request<Channel | null>('/channel'), request<Niche[]>('/week'), request<Production[]>('/productions')]);
    if (c) {
      setChannel(c);
      setPlannedDate(current => current || new Intl.DateTimeFormat('sv-SE', { timeZone: c.timeZone }).format(new Date()));
    }
    setNiches(n); setProductions(p);
    setNicheId(current => n.some(x => x.id === current && x.enabled) ? current : n.find(x => x.enabled)?.id ?? '');
  };
  useEffect(() => { void refresh().catch(e => setError(String(e.message ?? e))).finally(() => setLoading(false)); }, []);
  const run: SaveOperation = async operation => {
    if (busy.current) return;
    busy.current = true; setSaving(true); setError(''); setNotice('');
    try { await operation(); setNotice('Cambios guardados.'); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { busy.current = false; setSaving(false); }
  };
  const loadDetail = async (id: string) => {
    const version = ++selectionVersion.current;
    setDetailLoading(true);
    try { const detail = await request<Detail>('/productions/' + id); if (version === selectionVersion.current) setSelected(detail); }
    finally { if (version === selectionVersion.current) setDetailLoading(false); }
  };
  const open = (id: string) => { setSelected(null); setError(''); void loadDetail(id).catch(e => setError(String(e.message ?? e))); };
  const reload = async (id: string) => { await refresh(); await loadDetail(id); };
  const create = (e: FormEvent) => { e.preventDefault(); void run(async () => {
    const body = { contentNicheId: nicheId, topic, plannedDate };
    if (creationRequest.current.body !== JSON.stringify(body)) creationRequest.current = { body: JSON.stringify(body), key: crypto.randomUUID() };
    const p = await request<Production>('/productions', 'POST', body, { 'Idempotency-Key': creationRequest.current.key });
    setTopic(''); await reload(p.id);
  }); };
  const visible = productions.filter(p => (!statusFilter || p.status === statusFilter) && (!nicheFilter || p.contentNicheId === nicheFilter));
  const studioLink = (view: string) => { setTab('studio'); setStudioView(view); };
  return <div className="app-shell"><a className="skip-link" href="#main-content">Saltar al contenido</a><aside className="sidebar"><a className="brand" href="#main-content" onClick={() => studioLink('Resumen')}><svg viewBox="0 0 28 28" aria-hidden="true"><rect x="3" y="3" width="22" height="22" rx="5" fill="none" stroke="currentColor" strokeWidth="1.5"/><path d="M9 19V9l10 10V9" fill="none" stroke="currentColor" strokeWidth="2"/></svg><span>Notebook-yt</span></a><p className="sidebar-channel">{channel.name || 'Tu canal editorial'}<span>Espacio privado del operador</span></p><nav aria-label="Secciones editoriales"><p>Operación diaria</p>{[['Resumen', 'Centro de producción'], ['Calendario', 'Calendario'], ['Temas', 'Temas'], ['Producciones', 'Espacios de trabajo']].map(([view, label]) => <button key={view} aria-pressed={tab === 'studio' && studioView === view} onClick={() => studioLink(view)}>{label}</button>)}<button aria-pressed={tab === 'productions'} onClick={() => setTab('productions')}>Producciones</button><p>Configuración</p><button aria-pressed={tab === 'week'} onClick={() => setTab('week')}>Semana editorial</button>{[['Integraciones', 'Integraciones'], ['Ajustes', 'Ajustes'], ['Trabajos', 'Trabajos']].map(([view, label]) => <button key={view} aria-pressed={tab === 'studio' && studioView === view} onClick={() => studioLink(view)}>{label}</button>)}</nav><div className="sidebar-note"><strong>NotebookLM → YouTube</strong><span>Un vídeo original al día.<br/>Cada paso conserva su evidencia.</span></div></aside><main id="main-content" tabIndex={-1}><header className="page-header"><div><h1>{tab === 'week' ? 'Canal y nichos' : tab === 'productions' ? 'Planificar una producción' : studioView === 'Resumen' ? 'Tu mesa editorial' : studioView}</h1><p>{tab === 'week' ? 'Una semana completa, siete líneas editoriales.' : tab === 'productions' ? 'Crea una producción o revisa sus datos editoriales.' : 'De las fuentes al vídeo, con el siguiente paso siempre a mano.'}</p></div><span className="local-status">Entorno local · {channel.timeZone}</span></header>
    {loading && <p role="status">Cargando datos editoriales…</p>}
    {error && <div role="alert" className="error">{error}<button disabled={saving} onClick={() => void run(refresh)}>Reintentar carga</button></div>}
    {saving && <p role="status">Guardando cambios…</p>}{notice && <p role="status">{notice}</p>}
    {!loading && (tab === 'studio' ? <Studio view={studioView} onViewChange={setStudioView} embedded /> : tab === 'week' ? <>
      <section><h2>Configuración del canal</h2><form onSubmit={e => { e.preventDefault(); void run(async () => { await request('/channel', 'PUT', channel); await refresh(); }); }}>
        <fieldset disabled={saving}><legend>Canal local</legend><label>Nombre del canal<input required maxLength={160} value={channel.name} onChange={e => setChannel({ ...channel, name: e.target.value })} /></label>
          <label>Zona horaria IANA<input required maxLength={100} value={channel.timeZone} onChange={e => setChannel({ ...channel, timeZone: e.target.value })} placeholder="Europe/Madrid" /></label>
          <button disabled={!channel.name.trim()}>Guardar canal e inicializar semana</button></fieldset></form></section>
      <h2>Calendario semanal</h2><p>Las asignaciones desactivadas conservan sus producciones anteriores.</p>
      {!niches.length && <p>Configura el canal para generar las siete asignaciones.</p>}
      <div className="grid">{niches.map(n => <NicheEditor key={JSON.stringify(n)} niche={n} saving={saving} save={draft => void run(async () => { await request('/week/' + draft.weekday, 'PUT', draft); await refresh(); })} />)}</div>
    </> : <>
      <section><h2>Nueva producción</h2><form onSubmit={create}><fieldset disabled={saving}><legend>Planificación</legend>
        <label>Tema de trabajo<input required maxLength={300} value={topic} onChange={e => setTopic(e.target.value)} /></label>
        <label>Nicho para nueva producción<select required value={nicheId} onChange={e => setNicheId(e.target.value)}><option value="">Selecciona un nicho activo</option>{niches.filter(n => n.enabled).map(n => <option key={n.id} value={n.id}>{n.name}</option>)}</select></label>
        <label>Fecha prevista<input required type="date" value={plannedDate} onChange={e => setPlannedDate(e.target.value)} /></label>
        <button disabled={!topic.trim() || !nicheId || !plannedDate}>Crear producción</button></fieldset></form>
        {!niches.some(n => n.enabled) && <p>Activa un nicho en la semana editorial antes de crear una producción.</p>}
      </section>
      <section><h2>Producciones</h2><div className="filters"><label>Filtrar por estado<select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}><option value="">Todos los estados</option>{Object.keys(transitions).map(s => <option key={s}>{s}</option>)}</select></label>
        <label>Filtrar por nicho<select value={nicheFilter} onChange={e => setNicheFilter(e.target.value)}><option value="">Todos los nichos</option>{niches.map(n => <option key={n.id} value={n.id}>{n.name}{n.enabled ? '' : ' (desactivado)'}</option>)}</select></label></div>
        {!visible.length ? <p>No hay producciones para estos filtros.</p> : <ul className="production-list">{visible.map(p => <li key={p.id}><button className="link" disabled={saving} onClick={() => open(p.id)}>{p.topic}</button><span>{p.plannedDate} · {p.status} · {p.nicheNameSnapshot}</span></li>)}</ul>}
      </section>
      {detailLoading && <p role="status">Cargando detalle…</p>}
      {selected && <ProductionEditor key={`${selected.production.id}:${selected.production.modifiedAtUtc}`} detail={selected} saving={saving} run={run} reload={reload} />}
    </>)}
  </main></div>;
}
