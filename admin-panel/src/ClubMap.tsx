import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  getComputers,
  getComputerDetails,
  stopSession,
  startSession,
  shutdownPc,
  rebootPc,
  saveComputerLayout,
  type Computer,
  type ComputerDetails,
} from './api';
import { useClubLive } from './useClubLive';
import './ClubMap.css';

type GroupFilter = 'all' | 'common' | 'vip';
type MapStatus = 'offline' | 'locked' | 'active' | 'error';

interface Pos {
  x: number;
  y: number;
}

function snap(v: number, step = 2) {
  return Math.round(Math.max(0, Math.min(100, v)) / step) * step;
}

function remainingLabel(end: string | null): string | null {
  if (!end) return null;
  const ms = new Date(end).getTime() - Date.now();
  if (Number.isNaN(ms) || ms <= 0) return 'время вышло';
  const mins = Math.ceil(ms / 60000);
  if (mins < 60) return `${mins} мин`;
  const h = Math.floor(mins / 60);
  const m = mins % 60;
  return m ? `${h} ч ${m} мин` : `${h} ч`;
}

function resolveStatus(pc: Computer): MapStatus {
  if (pc.status === 'Error') return 'error';
  if (!pc.isOnline) return 'offline';
  if (pc.currentUser || pc.status === 'Active') return 'active';
  return 'locked';
}

function statusLabel(s: MapStatus) {
  switch (s) {
    case 'active': return 'Занят';
    case 'locked': return 'Свободен';
    case 'error': return 'Ошибка';
    default: return 'Оффлайн';
  }
}

function isVip(pc: Computer) {
  return pc.groupName === 'VIP Комната' || pc.nameToDisplay.toUpperCase().includes('VIP');
}

/** Автосетка для ПК без сохранённых координат. */
function autoLayout(pcs: Computer[]): Record<number, Pos> {
  const byGroup = new Map<string, Computer[]>();
  for (const pc of pcs) {
    const key = isVip(pc) ? 'VIP' : 'Common';
    if (!byGroup.has(key)) byGroup.set(key, []);
    byGroup.get(key)!.push(pc);
  }

  const result: Record<number, Pos> = {};
  let groupIndex = 0;
  for (const [, list] of byGroup) {
    const cols = Math.max(4, Math.ceil(Math.sqrt(list.length)));
    list.forEach((pc, i) => {
      const col = i % cols;
      const row = Math.floor(i / cols);
      const baseY = 18 + groupIndex * 42;
      result[pc.id] = {
        x: snap(12 + col * (76 / Math.max(cols - 1, 1))),
        y: snap(baseY + row * 14),
      };
    });
    groupIndex += 1;
  }
  return result;
}

function buildPositions(pcs: Computer[]): Record<number, Pos> {
  const auto = autoLayout(pcs);
  const pos: Record<number, Pos> = {};
  for (const pc of pcs) {
    if (pc.mapX != null && pc.mapY != null) {
      pos[pc.id] = { x: pc.mapX, y: pc.mapY };
    } else {
      pos[pc.id] = auto[pc.id] ?? { x: 50, y: 50 };
    }
  }
  return pos;
}

export default function ClubMap() {
  const role = localStorage.getItem('userRole') || 'Admin';
  const canEdit = role === 'Senior' || role === 'Super';

  const [computers, setComputers] = useState<Computer[]>([]);
  const [filter, setFilter] = useState<GroupFilter>('all');
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [details, setDetails] = useState<ComputerDetails | null>(null);
  const [detailsLoading, setDetailsLoading] = useState(false);

  const [editing, setEditing] = useState(false);
  const [positions, setPositions] = useState<Record<number, Pos>>({});
  const [savedSnapshot, setSavedSnapshot] = useState<Record<number, Pos>>({});
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);

  const canvasRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{ id: number; moved: boolean } | null>(null);
  const [draggingId, setDraggingId] = useState<number | null>(null);

  const fetchComputers = useCallback(async () => {
    try {
      const data = await getComputers();
      const approved = (Array.isArray(data) ? data : []).filter(c => c.isApproved);
      setComputers(approved);
      if (!editing) {
        setPositions(buildPositions(approved));
      } else {
        // В edit не сбрасываем локальные позиции, но добавляем новых ПК.
        setPositions(prev => {
          const next = { ...prev };
          const auto = autoLayout(approved);
          for (const pc of approved) {
            if (!next[pc.id]) {
              next[pc.id] = pc.mapX != null && pc.mapY != null
                ? { x: pc.mapX, y: pc.mapY }
                : (auto[pc.id] ?? { x: 50, y: 50 });
            }
          }
          return next;
        });
      }
    } catch (e) {
      console.error('Ошибка загрузки карты', e);
    }
  }, [editing]);

  useEffect(() => {
    fetchComputers();
    const t = setInterval(fetchComputers, 15000);
    return () => clearInterval(t);
  }, [fetchComputers]);

  useClubLive(['computers', 'dashboard'], fetchComputers);

  const filtered = useMemo(() => {
    return computers.filter(pc => {
      if (filter === 'vip') return isVip(pc);
      if (filter === 'common') return !isVip(pc);
      return true;
    });
  }, [computers, filter]);

  const stats = useMemo(() => {
    let online = 0, busy = 0, free = 0, errors = 0, offline = 0;
    for (const pc of computers) {
      const s = resolveStatus(pc);
      if (s === 'offline') offline += 1;
      else online += 1;
      if (s === 'active') busy += 1;
      if (s === 'locked') free += 1;
      if (s === 'error') errors += 1;
    }
    return { online, busy, free, errors, offline, total: computers.length };
  }, [computers]);

  const selected = computers.find(c => c.id === selectedId) || null;

  useEffect(() => {
    if (!selected) {
      setDetails(null);
      return;
    }
    let cancelled = false;
    setDetailsLoading(true);
    getComputerDetails(selected.pcName)
      .then(d => { if (!cancelled) setDetails(d); })
      .catch(() => { if (!cancelled) setDetails(null); })
      .finally(() => { if (!cancelled) setDetailsLoading(false); });
    return () => { cancelled = true; };
  }, [selected?.pcName]);

  const startEdit = () => {
    if (!canEdit) return;
    const pos = buildPositions(computers);
    setPositions(pos);
    setSavedSnapshot(JSON.parse(JSON.stringify(pos)));
    setDirty(false);
    setEditing(true);
  };

  const cancelEdit = () => {
    setPositions(savedSnapshot);
    setDirty(false);
    setEditing(false);
    setDraggingId(null);
    dragRef.current = null;
  };

  const saveEdit = async () => {
    setSaving(true);
    try {
      const items = computers.map(pc => ({
        id: pc.id,
        mapX: positions[pc.id]?.x ?? 50,
        mapY: positions[pc.id]?.y ?? 50,
      }));
      await saveComputerLayout(items);
      setSavedSnapshot(JSON.parse(JSON.stringify(positions)));
      setDirty(false);
      setEditing(false);
      await fetchComputers();
    } catch (e) {
      alert('Не удалось сохранить расстановку');
      console.error(e);
    } finally {
      setSaving(false);
    }
  };

  const clientToPercent = (clientX: number, clientY: number): Pos | null => {
    const el = canvasRef.current;
    if (!el) return null;
    const rect = el.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return null;
    const x = ((clientX - rect.left) / rect.width) * 100;
    const y = ((clientY - rect.top) / rect.height) * 100;
    return { x: snap(x), y: snap(y) };
  };

  const onPointerDown = (e: React.PointerEvent, pc: Computer) => {
    if (!editing) {
      setSelectedId(pc.id);
      return;
    }
    e.preventDefault();
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    dragRef.current = { id: pc.id, moved: false };
    setDraggingId(pc.id);
    setSelectedId(pc.id);
  };

  const onPointerMove = (e: React.PointerEvent) => {
    if (!editing || !dragRef.current) return;
    const pos = clientToPercent(e.clientX, e.clientY);
    if (!pos) return;
    dragRef.current.moved = true;
    const id = dragRef.current.id;
    setPositions(prev => ({ ...prev, [id]: pos }));
    setDirty(true);
  };

  const onPointerUp = () => {
    dragRef.current = null;
    setDraggingId(null);
  };

  const runAction = async (fn: () => Promise<unknown>, confirmMsg?: string) => {
    if (confirmMsg && !confirm(confirmMsg)) return;
    try {
      await fn();
      await fetchComputers();
    } catch (e) {
      console.error(e);
      alert('Действие не выполнено');
    }
  };

  return (
    <div className="club-map-page">
      <div className="club-map-toolbar">
        <div className="club-map-stats">
          <div className="club-map-stat"><span className="dot online" />Онлайн <b>{stats.online}</b></div>
          <div className="club-map-stat"><span className="dot busy" />Занято <b>{stats.busy}</b></div>
          <div className="club-map-stat"><span className="dot free" />Свободно <b>{stats.free}</b></div>
          <div className="club-map-stat"><span className="dot offline" />Оффлайн <b>{stats.offline}</b></div>
          {stats.errors > 0 && (
            <div className="club-map-stat"><span className="dot error" />Ошибки <b>{stats.errors}</b></div>
          )}
        </div>

        <div className="page-actions">
          <div className="club-map-filters ui-tabs" style={{ marginBottom: 0 }}>
            <button type="button" className={`ui-tab ${filter === 'all' ? 'active' : ''}`} onClick={() => setFilter('all')}>Все</button>
            <button type="button" className={`ui-tab ${filter === 'common' ? 'active' : ''}`} onClick={() => setFilter('common')}>Общий зал</button>
            <button type="button" className={`ui-tab ${filter === 'vip' ? 'active' : ''}`} onClick={() => setFilter('vip')}>VIP</button>
          </div>

          {canEdit && !editing && (
            <button type="button" className="ui-btn" onClick={startEdit}>Редактировать схему</button>
          )}
          {editing && (
            <>
              <button type="button" className="ui-btn" onClick={cancelEdit} disabled={saving}>Отмена</button>
              <button type="button" className="ui-btn ui-btn-primary" onClick={saveEdit} disabled={saving || !dirty}>
                {saving ? 'Сохранение…' : 'Сохранить'}
              </button>
            </>
          )}
        </div>
      </div>

      {editing && (
        <div className="map-edit-banner">
          Режим редактирования: перетащите ПК на схеме, затем сохраните расстановку.
        </div>
      )}

      <div className="club-map-body">
        <div className="club-map-canvas-wrap">
          <div
            ref={canvasRef}
            className={`club-map-canvas ${editing ? 'editing' : ''}`}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerLeave={onPointerUp}
          >
            {filtered.map(pc => {
              const pos = positions[pc.id] ?? { x: 50, y: 50 };
              const status = resolveStatus(pc);
              const left = remainingLabel(pc.sessionEndTime);
              const meta = pc.currentUser
                ? `${pc.currentUser}${left ? ` · ${left}` : ''}`
                : (pc.currentAppTitle || pc.currentApp || statusLabel(status));

              return (
                <div
                  key={pc.id}
                  className={[
                    'map-pc',
                    `status-${status}`,
                    selectedId === pc.id ? 'selected' : '',
                    draggingId === pc.id ? 'dragging' : '',
                  ].filter(Boolean).join(' ')}
                  style={{ left: `${pos.x}%`, top: `${pos.y}%` }}
                  onPointerDown={e => onPointerDown(e, pc)}
                >
                  <div className="map-pc-name">{pc.nameToDisplay}</div>
                  <div className="map-pc-meta">{meta}</div>
                  <div className="map-pc-status-row">
                    <span className="sdot" />
                    {statusLabel(status)}
                  </div>
                </div>
              );
            })}

            {filtered.length === 0 && (
              <div className="club-map-side-empty" style={{ position: 'absolute', inset: 0 }}>
                Нет утверждённых ПК в этом зале
              </div>
            )}
          </div>
        </div>

        <aside className="club-map-side">
          {!selected ? (
            <div className="club-map-side-empty">Выберите компьютер на карте</div>
          ) : (
            <>
              <div className="club-map-side-head">
                <div>
                  <h3>{selected.nameToDisplay}</h3>
                  <p className="page-subtitle" style={{ marginTop: 4 }}>
                    {selected.groupName} · {statusLabel(resolveStatus(selected))}
                  </p>
                </div>
                <button type="button" className="ui-btn ui-btn-ghost ui-btn-sm" onClick={() => setSelectedId(null)}>Закрыть</button>
              </div>
              <div className="club-map-side-body">
                <div className="map-detail-grid">
                  <div className="map-detail-item">
                    <span>Клиент</span>
                    <b>{selected.currentUser || '—'}</b>
                  </div>
                  <div className="map-detail-item">
                    <span>Осталось</span>
                    <b>{remainingLabel(selected.sessionEndTime) || '—'}</b>
                  </div>
                  <div className="map-detail-item full">
                    <span>Приложение</span>
                    <b>{selected.currentAppTitle || selected.currentApp || '—'}</b>
                  </div>
                </div>

                {detailsLoading && <p className="page-subtitle">Загрузка железа…</p>}
                {details && !detailsLoading && (
                  <div className="map-detail-grid">
                    <div className="map-detail-item full"><span>CPU</span><b>{details.cpuName || '—'}</b></div>
                    <div className="map-detail-item"><span>RAM</span><b>{details.ramTotalMb ? `${(details.ramTotalMb / 1024).toFixed(1)} ГБ` : '—'}</b></div>
                    <div className="map-detail-item"><span>IP</span><b>{details.ipAddress || '—'}</b></div>
                    <div className="map-detail-item full"><span>GPU</span><b>{details.gpuName || '—'}</b></div>
                  </div>
                )}

                {!editing && (
                  <div className="map-actions">
                    {selected.currentUser ? (
                      <button
                        type="button"
                        className="ui-btn ui-btn-danger"
                        onClick={() => runAction(() => stopSession(selected.pcName), `Завершить сессию на ${selected.nameToDisplay}?`)}
                      >
                        Завершить
                      </button>
                    ) : (
                      <button
                        type="button"
                        className="ui-btn ui-btn-primary"
                        onClick={() => {
                          const min = prompt('На сколько минут открыть (бесплатно)?', '60');
                          if (!min) return;
                          runAction(() => startSession(selected.pcName, Number(min)));
                        }}
                      >
                        Открыть
                      </button>
                    )}
                    <button
                      type="button"
                      className="ui-btn"
                      onClick={() => runAction(() => rebootPc(selected.pcName), `Перезагрузить ${selected.nameToDisplay}?`)}
                    >
                      Reboot
                    </button>
                    <button
                      type="button"
                      className="ui-btn"
                      onClick={() => runAction(() => shutdownPc(selected.pcName), `Выключить ${selected.nameToDisplay}?`)}
                    >
                      Shutdown
                    </button>
                  </div>
                )}
              </div>
            </>
          )}
        </aside>
      </div>
    </div>
  );
}
