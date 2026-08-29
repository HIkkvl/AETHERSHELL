import { useEffect, useState } from "react";
import {
    adjustClientWallet,
    getClientGroups,
    getUserProfile,
    type ClientGroup,
    type UserProfile,
} from "./api";
import "./UserProfilePanel.css";

type Tab = 'info' | 'balance' | 'sessions' | 'orders' | 'logs';

const TABS: { id: Tab; label: string }[] = [
    { id: 'info', label: 'Инфо' },
    { id: 'balance', label: 'Баланс' },
    { id: 'sessions', label: 'Сессии' },
    { id: 'orders', label: 'Заказы' },
    { id: 'logs', label: 'Логи' },
];

const ORDER_STATUS_LABELS: Record<string, string> = {
    New: 'Новый',
    Processing: 'Готовится',
    Ready: 'Готов',
    Completed: 'Выдан',
    Cancelled: 'Отменён',
};

function formatMoney(value: number) {
    return `${value.toLocaleString('ru-RU')} ₸`;
}

function formatDateTime(value: string) {
    return new Date(value).toLocaleString('ru-RU', {
        day: '2-digit', month: '2-digit', year: '2-digit',
        hour: '2-digit', minute: '2-digit'
    });
}

function formatDuration(start: string, end: string) {
    const minutes = Math.max(0, Math.round((new Date(end).getTime() - new Date(start).getTime()) / 60000));
    if (minutes < 60) return `${minutes} мин`;
    const hours = Math.floor(minutes / 60);
    const rest = minutes % 60;
    return rest === 0 ? `${hours} ч` : `${hours} ч ${rest} мин`;
}

interface Props {
    userId: number;
    onClose: () => void;
    onUpdated?: () => void;
}

export default function UserProfilePanel({ userId, onClose, onUpdated }: Props) {
    const [profile, setProfile] = useState<UserProfile | null>(null);
    const [groups, setGroups] = useState<ClientGroup[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [tab, setTab] = useState<Tab>('info');

    const [editBalance, setEditBalance] = useState('');
    const [editDiscount, setEditDiscount] = useState('');
    const [editMinutes, setEditMinutes] = useState('');
    const [editGroupId, setEditGroupId] = useState('');
    const [saving, setSaving] = useState(false);
    const [saveMsg, setSaveMsg] = useState<string | null>(null);
    const [saveError, setSaveError] = useState<string | null>(null);

    const loadProfile = async () => {
        setLoading(true);
        setError(null);
        try {
            const [data, groupList] = await Promise.all([
                getUserProfile(userId),
                getClientGroups(),
            ]);
            setProfile(data);
            setGroups(groupList);
            setEditBalance(String(data.balance));
            setEditDiscount(String(data.discountPercent));
            setEditMinutes(String(data.remainingMinutes));
            setEditGroupId(data.groupId != null ? String(data.groupId) : '');
        } catch {
            setError('Не удалось загрузить профиль');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        void loadProfile();
    }, [userId]);

    useEffect(() => {
        const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [onClose]);

    const saveWallet = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!profile) return;

        const balance = Number(editBalance);
        const discountPercent = Number(editDiscount);
        const remainingMinutes = Number(editMinutes);

        if (!Number.isFinite(balance) || balance < 0) {
            setSaveError('Некорректный баланс');
            return;
        }
        if (!Number.isFinite(discountPercent) || discountPercent < 0 || discountPercent > profile.maxDiscountPercent) {
            setSaveError(`Скидка от 0 до ${profile.maxDiscountPercent}%`);
            return;
        }
        if (!Number.isFinite(remainingMinutes) || remainingMinutes < 0 || !Number.isInteger(remainingMinutes)) {
            setSaveError('Минуты — целое число ≥ 0');
            return;
        }

        const clearGroup = editGroupId === '';
        const groupId = clearGroup ? undefined : Number(editGroupId);

        setSaving(true);
        setSaveMsg(null);
        setSaveError(null);
        try {
            await adjustClientWallet(profile.id, {
                balance,
                discountPercent: Math.round(discountPercent),
                remainingMinutes,
                ...(clearGroup ? { clearGroup: true } : { groupId }),
            });
            setSaveMsg('Сохранено');
            await loadProfile();
            onUpdated?.();
        } catch (err: any) {
            const msg = err?.response?.data;
            setSaveError(typeof msg === 'string' ? msg : (msg?.error || 'Не удалось сохранить'));
        } finally {
            setSaving(false);
        }
    };

    return (
        <div className="profile-backdrop" onClick={onClose}>
            <aside className="profile-panel" onClick={(e) => e.stopPropagation()}>
                <button className="profile-close" onClick={onClose} title="Закрыть">×</button>

                {loading && <div className="profile-loading">Загрузка...</div>}
                {error && <div className="profile-loading">{error}</div>}

                {profile && !loading && (
                    <>
                        <header className="profile-head">
                            <div className="profile-avatar">{profile.username.slice(0, 2).toUpperCase()}</div>
                            <div>
                                <h2>{profile.username}</h2>
                                <div className="profile-sub">
                                    {profile.currentPcDisplay
                                        ? <span className="profile-online">🟢 {profile.currentPcDisplay}</span>
                                        : <span className="profile-offline">⚪ Не в сети</span>}
                                    {profile.groupName && (
                                        <span
                                            className="profile-group-badge"
                                            style={{
                                                background: `${profile.groupColor || '#6B7280'}22`,
                                                color: profile.groupColor || '#6B7280',
                                                borderColor: `${profile.groupColor || '#6B7280'}55`,
                                            }}
                                        >
                                            {profile.groupName}
                                        </span>
                                    )}
                                </div>
                            </div>
                        </header>

                        <nav className="profile-tabs">
                            {TABS.map(t => (
                                <button
                                    key={t.id}
                                    className={`profile-tab ${tab === t.id ? 'active' : ''}`}
                                    onClick={() => setTab(t.id)}
                                >
                                    {t.label}
                                </button>
                            ))}
                        </nav>

                        <div className="profile-body">
                            {tab === 'info' && (
                                <>
                                    <div className="profile-row">
                                        <span>Логин</span><b>{profile.username}</b>
                                    </div>
                                    <div className="profile-row">
                                        <span>Email</span><b>{profile.email || '—'}</b>
                                    </div>
                                    <div className="profile-row">
                                        <span>Группа</span><b>{profile.groupName || '—'}</b>
                                    </div>
                                    <div className="profile-row">
                                        <span>Роль</span><b>{profile.role || 'Client'}</b>
                                    </div>
                                    <div className="profile-row">
                                        <span>Регистрация</span><b>{formatDateTime(profile.createdAt)}</b>
                                    </div>
                                    <div className="profile-row">
                                        <span>Всего сессий</span><b>{profile.totalSessions}</b>
                                    </div>
                                    {profile.currentApp && (
                                        <div className="profile-row">
                                            <span>Сейчас играет</span><b>{profile.currentApp}</b>
                                        </div>
                                    )}
                                    {profile.sessionEndTime && (
                                        <div className="profile-row">
                                            <span>Сессия до</span><b>{formatDateTime(profile.sessionEndTime)}</b>
                                        </div>
                                    )}
                                </>
                            )}

                            {tab === 'balance' && (
                                <>
                                    <div className="profile-cards">
                                        <div className="profile-card">
                                            <span>Потрачено во всей сети</span>
                                            <b>{formatMoney(profile.totalSpent)}</b>
                                        </div>
                                        <div className="profile-card">
                                            <span>Текущая скидка</span>
                                            <b>{profile.discountPercent}%</b>
                                        </div>
                                    </div>

                                    <form className="profile-wallet-form" onSubmit={saveWallet}>
                                        <label>
                                            Группа
                                            <select
                                                value={editGroupId}
                                                onChange={(e) => setEditGroupId(e.target.value)}
                                            >
                                                <option value="">Без группы</option>
                                                {groups.map(g => (
                                                    <option key={g.id} value={g.id}>
                                                        {g.name}
                                                        {g.discountPercent != null ? ` (${g.discountPercent}%)` : ''}
                                                    </option>
                                                ))}
                                            </select>
                                        </label>
                                        <label>
                                            Баланс, ₸
                                            <input
                                                type="number"
                                                min={0}
                                                step="0.01"
                                                value={editBalance}
                                                onChange={(e) => setEditBalance(e.target.value)}
                                                required
                                            />
                                        </label>
                                        <label>
                                            Скидка, % (макс. {profile.maxDiscountPercent})
                                            <input
                                                type="number"
                                                min={0}
                                                max={profile.maxDiscountPercent}
                                                step={1}
                                                value={editDiscount}
                                                onChange={(e) => setEditDiscount(e.target.value)}
                                                required
                                            />
                                        </label>
                                        <label>
                                            Несгораемый остаток минут
                                            <input
                                                type="number"
                                                min={0}
                                                step={1}
                                                value={editMinutes}
                                                onChange={(e) => setEditMinutes(e.target.value)}
                                                required
                                            />
                                        </label>

                                        {saveError && <p className="profile-wallet-error">{saveError}</p>}
                                        {saveMsg && <p className="profile-wallet-ok">{saveMsg}</p>}

                                        <button type="submit" className="profile-action" disabled={saving}>
                                            {saving ? 'Сохранение…' : 'Сохранить'}
                                        </button>
                                    </form>

                                    <p className="profile-hint">
                                        Несгораемый остаток минут возвращается в профиль только с несгораемых пакетов.
                                        Сгораемое время при выходе сгорает.
                                    </p>

                                    <p className="profile-hint">
                                        {profile.discountOverride != null
                                            ? 'Скидка задана вручную и перекрывает группу и лояльность.'
                                            : profile.groupDiscountPercent != null
                                                ? `Скидка из группы «${profile.groupName}» (${profile.groupDiscountPercent}%).`
                                                : profile.nextThreshold !== null
                                                    ? `До следующего процента скидки: ${formatMoney(Math.ceil(profile.nextThreshold))}`
                                                    : `Достигнут максимум скидки — ${profile.maxDiscountPercent}%`}
                                    </p>

                                    {profile.networkClubs > 1 && (
                                        <p className="profile-hint">
                                            Баланс, минуты, группа и скидка общие для всех филиалов сети
                                            ({profile.networkClubs}). История ниже — только по этому залу.
                                        </p>
                                    )}
                                </>
                            )}

                            {tab === 'sessions' && (
                                profile.sessions.length === 0
                                    ? <p className="profile-empty">Сессий пока не было</p>
                                    : <table className="profile-table">
                                        <thead>
                                            <tr><th>ПК</th><th>Начало</th><th>Длительность</th><th>Сумма</th></tr>
                                        </thead>
                                        <tbody>
                                            {profile.sessions.map(s => (
                                                <tr key={s.id}>
                                                    <td>{s.computerName}</td>
                                                    <td>{formatDateTime(s.startTime)}</td>
                                                    <td>{s.isActive ? 'идёт' : formatDuration(s.startTime, s.endTime)}</td>
                                                    <td>{formatMoney(s.price)}</td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                            )}

                            {tab === 'orders' && (
                                profile.orders.length === 0
                                    ? <p className="profile-empty">Заказов пока не было</p>
                                    : <table className="profile-table">
                                        <thead>
                                            <tr><th>Когда</th><th>Состав</th><th>Статус</th><th>Сумма</th></tr>
                                        </thead>
                                        <tbody>
                                            {profile.orders.map(o => (
                                                <tr key={o.id}>
                                                    <td>{formatDateTime(o.createdAt)}</td>
                                                    <td>{o.items.map(i => `${i.productNameSnapshot} ×${i.quantity}`).join(', ')}</td>
                                                    <td>{ORDER_STATUS_LABELS[o.status] || o.status}</td>
                                                    <td>{formatMoney(o.totalPrice)}</td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                            )}

                            {tab === 'logs' && (
                                profile.logs.length === 0
                                    ? <p className="profile-empty">Действий по этому клиенту нет</p>
                                    : <ul className="profile-logs">
                                        {profile.logs.map(l => (
                                            <li key={l.id}>
                                                <div className="log-top">
                                                    <b>{l.adminName}</b>
                                                    <span>{formatDateTime(l.createdAt)}</span>
                                                </div>
                                                <div className="log-details">{l.details}</div>
                                            </li>
                                        ))}
                                    </ul>
                            )}
                        </div>
                    </>
                )}
            </aside>
        </div>
    );
}
