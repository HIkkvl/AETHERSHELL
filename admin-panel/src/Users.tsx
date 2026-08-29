import React, { useEffect, useState } from "react";
import {
    getClients,
    getStaff,
    getClientGroups,
    createClient,
    createStaff,
    createClientGroup,
    updateClientGroup,
    deleteClient,
    deleteStaff,
    deleteClientGroup,
    type User,
    type ClientGroup
} from "./api";
import UserProfilePanel from "./UserProfilePanel";
import { useClubLive } from "./useClubLive";
import "./Users.css";

interface UsersProps {
    mode: 'clients' | 'staff';
}

type GroupFilter = 'all' | 'ungrouped' | number;

const GROUP_COLORS = ['#6B7280', '#2563EB', '#059669', '#D97706', '#DC2626', '#7C3AED', '#0891B2'];

export default function Users({ mode }: UsersProps) {
    const [users, setUsers] = useState<User[]>([]);
    const [loading, setLoading] = useState(true);
    const [groups, setGroups] = useState<ClientGroup[]>([]);

    const [isModalOpen, setIsModalOpen] = useState(false);
    const [groupsModalOpen, setGroupsModalOpen] = useState(false);
    const [searchTerm, setSearchTerm] = useState("");
    const [groupFilter, setGroupFilter] = useState<GroupFilter>('all');

    const [username, setUsername] = useState("");
    const [password, setPassword] = useState("");
    const [role, setRole] = useState("Admin");

    const [profileUser, setProfileUser] = useState<User | null>(null);

    const [editGroupId, setEditGroupId] = useState<number | null>(null);
    const [gName, setGName] = useState("");
    const [gColor, setGColor] = useState(GROUP_COLORS[0]);
    const [gDiscount, setGDiscount] = useState("");
    const [gSaving, setGSaving] = useState(false);
    const [gError, setGError] = useState<string | null>(null);

    const fetchGroups = async () => {
        if (mode !== 'clients') return;
        try {
            setGroups(await getClientGroups());
        } catch (e) {
            console.error("Ошибка загрузки групп", e);
        }
    };

    const fetchUsers = async () => {
        setLoading(true);
        try {
            if (mode === 'staff') {
                setUsers(await getStaff(searchTerm));
            } else {
                const opts =
                    groupFilter === 'ungrouped' ? { ungrouped: true }
                    : typeof groupFilter === 'number' ? { groupId: groupFilter }
                    : undefined;
                setUsers(await getClients(searchTerm, opts));
            }
        } catch (e) {
            console.error("Ошибка загрузки пользователей", e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        setSearchTerm("");
        setGroupFilter('all');
        fetchUsers();
        void fetchGroups();
    }, [mode]);

    useEffect(() => {
        if (mode === 'clients') void fetchUsers();
    }, [groupFilter]);

    useClubLive('clients', () => {
        if (mode === 'clients') {
            void fetchUsers();
            void fetchGroups();
        }
    });

    const handleSearchKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') fetchUsers();
    };

    const openCreateModal = () => {
        setUsername("");
        setPassword("");
        setRole("Admin");
        setIsModalOpen(true);
    };

    const openGroupsModal = () => {
        setEditGroupId(null);
        setGName("");
        setGColor(GROUP_COLORS[0]);
        setGDiscount("");
        setGError(null);
        setGroupsModalOpen(true);
        void fetchGroups();
    };

    const startEditGroup = (g: ClientGroup) => {
        setEditGroupId(g.id);
        setGName(g.name);
        setGColor(g.color || GROUP_COLORS[0]);
        setGDiscount(g.discountPercent != null ? String(g.discountPercent) : "");
        setGError(null);
    };

    const resetGroupForm = () => {
        setEditGroupId(null);
        setGName("");
        setGColor(GROUP_COLORS[0]);
        setGDiscount("");
        setGError(null);
    };

    const handleSaveGroup = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!gName.trim()) {
            setGError("Укажите название");
            return;
        }

        let discountPercent: number | null = null;
        if (gDiscount.trim() !== "") {
            const n = Number(gDiscount);
            if (!Number.isFinite(n) || n < 0 || !Number.isInteger(n)) {
                setGError("Скидка — целое число ≥ 0");
                return;
            }
            discountPercent = n;
        }

        setGSaving(true);
        setGError(null);
        try {
            const payload = {
                name: gName.trim(),
                color: gColor,
                discountPercent,
            };
            if (editGroupId != null) {
                await updateClientGroup(editGroupId, payload);
            } else {
                await createClientGroup(payload);
            }
            resetGroupForm();
            await fetchGroups();
            await fetchUsers();
        } catch (err: any) {
            setGError(err?.response?.data ?? "Не удалось сохранить группу");
        } finally {
            setGSaving(false);
        }
    };

    const handleDeleteGroup = async (g: ClientGroup) => {
        if (!confirm(`Удалить группу «${g.name}»? Клиенты останутся без группы.`)) return;
        try {
            await deleteClientGroup(g.id);
            if (groupFilter === g.id) setGroupFilter('all');
            if (editGroupId === g.id) resetGroupForm();
            await fetchGroups();
            await fetchUsers();
        } catch {
            alert("Ошибка удаления группы");
        }
    };

    const handleCreate = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            if (mode === 'staff') {
                await createStaff(username, password, role);
            } else {
                await createClient(username, password);
            }
            setIsModalOpen(false);
            fetchUsers();
        } catch (e: any) {
            alert(e?.response?.data ?? "Ошибка создания пользователя");
        }
    };

    const handleDelete = async (username: string) => {
        const target = users.find(u => u.username === username);
        if (!target) return;

        const question = mode === 'staff'
            ? `Удалить сотрудника ${username}?`
            : `Удалить клиента ${username} из всей сети вместе с балансом?`;
        if (!confirm(question)) return;

        try {
            if (mode === 'staff') {
                await deleteStaff(target.id);
            } else {
                await deleteClient(target.id);
            }
            if (profileUser?.id === target.id) setProfileUser(null);
            fetchUsers();
            if (mode === 'clients') void fetchGroups();
        } catch(e) { alert("Ошибка удаления"); }
    };

    return (
        <div className="page users-container">
            <div className="page-toolbar users-header-row">
                <p className="page-subtitle">{mode === 'staff' ? 'Сотрудники' : 'Клиенты клуба'}</p>
                <div className="page-actions">
                    <div className="search-box" style={{ display: 'flex', gap: '5px' }}>
                        <input
                            type="text"
                            placeholder="Поиск..."
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            onKeyDown={handleSearchKeyDown}
                            className="search-input"
                        />
                        <button onClick={fetchUsers} className="btn-search" type="button">
                            Найти
                        </button>
                    </div>

                    {mode === 'clients' && (
                        <button className="btn-search" type="button" onClick={openGroupsModal}>
                            Группы
                        </button>
                    )}

                    <button className="btn-plus-round" onClick={openCreateModal} title="Добавить">+</button>
                </div>
            </div>

            {mode === 'clients' && (
                <div className="users-group-filters">
                    <button
                        type="button"
                        className={`users-group-chip ${groupFilter === 'all' ? 'active' : ''}`}
                        onClick={() => setGroupFilter('all')}
                    >
                        Все
                    </button>
                    {groups.map(g => (
                        <button
                            key={g.id}
                            type="button"
                            className={`users-group-chip ${groupFilter === g.id ? 'active' : ''}`}
                            onClick={() => setGroupFilter(g.id)}
                            style={{ ['--chip-color' as string]: g.color }}
                        >
                            <span className="users-group-dot" style={{ background: g.color }} />
                            {g.name}
                            {g.discountPercent != null ? ` · ${g.discountPercent}%` : ''}
                        </button>
                    ))}
                    <button
                        type="button"
                        className={`users-group-chip ${groupFilter === 'ungrouped' ? 'active' : ''}`}
                        onClick={() => setGroupFilter('ungrouped')}
                    >
                        Без группы
                    </button>
                </div>
            )}

            <div className="users-list-card">
                {loading ? (
                    <p style={{padding:'20px', color:'var(--text-secondary)'}}>Загрузка...</p>
                ) : users.length === 0 ? (
                    <div style={{padding:'40px', textAlign:'center', color:'var(--text-secondary)'}}>
                        Список пуст
                    </div>
                ) : (
                    <table className="users-table">
                        <thead>
                            <tr>
                                <th>Имя пользователя</th>
                                {mode === 'clients' && <th>Группа</th>}
                                {mode === 'clients' && <th>Email</th>}
                                {mode === 'staff' && <th>Роль</th>}
                                {mode === 'clients' && <th>Статус</th>}
                                {mode === 'clients' && <th>Баланс сети</th>}
                                <th>Дата регистрации</th>
                                <th style={{textAlign: 'right'}}>Действия</th>
                            </tr>
                        </thead>
                        <tbody>
                            {users.map(u => (
                                <tr
                                    key={u.id}
                                    className={mode === 'clients' ? 'user-row' : undefined}
                                    onClick={mode === 'clients' ? () => setProfileUser(u) : undefined}
                                    title={mode === 'clients' ? 'Открыть профиль' : undefined}
                                >
                                    <td className="username">{u.username}</td>
                                    {mode === 'clients' && (
                                        <td>
                                            {u.groupName ? (
                                                <span
                                                    className="client-group-badge"
                                                    style={{
                                                        background: `${u.groupColor || '#6B7280'}22`,
                                                        color: u.groupColor || '#6B7280',
                                                        borderColor: `${u.groupColor || '#6B7280'}55`,
                                                    }}
                                                >
                                                    {u.groupName}
                                                </span>
                                            ) : (
                                                <span className="client-group-none">—</span>
                                            )}
                                        </td>
                                    )}
                                    {mode === 'clients' && (
                                        <td style={{color: u.email ? 'var(--text-secondary)' : '#666', fontSize: '13px'}}>
                                            {u.email || '—'}
                                        </td>
                                    )}
                                    {mode === 'staff' && (
                                        <td><span className={`role-badge ${u.role}`}>{u.role}</span></td>
                                    )}

                                    {mode === 'clients' && (
                                        <td>
                                            {u.currentPcDisplay ? (
                                                <span className="status-online">🟢 {u.currentPcDisplay}</span>
                                            ) : (
                                                <span className="status-offline">⚪ Offline</span>
                                            )}
                                        </td>
                                    )}

                                    {mode === 'clients' && <td className="balance">{u.balance} ₸</td>}
                                    <td style={{color:'var(--text-secondary)', fontSize:'13px'}}>
                                        {new Date(u.createdAt).toLocaleDateString()}
                                    </td>
                                    <td style={{textAlign: 'right'}} onClick={(e) => e.stopPropagation()}>
                                        <button className="btn-icon del" onClick={() => handleDelete(u.username)}>
                                            🗑
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>

            {profileUser && (
                <UserProfilePanel
                    userId={profileUser.id}
                    onClose={() => setProfileUser(null)}
                    onUpdated={() => {
                        void fetchUsers();
                        void fetchGroups();
                    }}
                />
            )}

            {isModalOpen && (
                <div className="modal-overlay" onClick={(e) => { if(e.target === e.currentTarget) setIsModalOpen(false) }}>
                    <div className="modal-content">
                        <button className="modal-close" onClick={() => setIsModalOpen(false)}>×</button>

                        <h3 className="modal-title">Новый {mode === 'staff' ? 'сотрудник' : 'клиент'}</h3>
                        <form onSubmit={handleCreate}>
                            <div className="form-group">
                                <label>Логин</label>
                                <input className="form-input" value={username} onChange={e => setUsername(e.target.value)} required autoFocus />
                            </div>
                            <div className="form-group">
                                <label>Пароль</label>
                                <input type="password" className="form-input" value={password} onChange={e => setPassword(e.target.value)} required />
                            </div>
                            {mode === 'staff' && (
                                <div className="form-group">
                                    <label>Роль</label>
                                    <select className="form-input" value={role} onChange={e => setRole(e.target.value)}>
                                        <option value="Admin">Администратор</option>
                                        <option value="Senior">Старший админ</option>
                                        <option value="Super">Управляющий</option>
                                    </select>
                                </div>
                            )}
                            <button type="submit" className="btn-submit">Создать</button>
                        </form>
                    </div>
                </div>
            )}

            {groupsModalOpen && (
                <div className="modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) setGroupsModalOpen(false); }}>
                    <div className="modal-content groups-modal">
                        <button className="modal-close" onClick={() => setGroupsModalOpen(false)}>×</button>
                        <h3 className="modal-title">Группы клиентов</h3>
                        <p className="groups-modal-hint">
                            VIP, скидки и метки общие на всю сеть. Скидка группы действует, если у клиента нет ручной.
                        </p>

                        <ul className="groups-list">
                            {groups.length === 0 && (
                                <li className="groups-empty">Пока нет групп</li>
                            )}
                            {groups.map(g => (
                                <li key={g.id} className="groups-list-item">
                                    <span className="users-group-dot" style={{ background: g.color }} />
                                    <div className="groups-list-main">
                                        <strong>{g.name}</strong>
                                        <span>
                                            {g.discountPercent != null ? `скидка ${g.discountPercent}%` : 'без фикс. скидки'}
                                            {` · ${g.clientsCount ?? 0} чел.`}
                                        </span>
                                    </div>
                                    <button type="button" className="btn-search" onClick={() => startEditGroup(g)}>Изменить</button>
                                    <button type="button" className="btn-icon del" onClick={() => handleDeleteGroup(g)}>🗑</button>
                                </li>
                            ))}
                        </ul>

                        <form className="groups-form" onSubmit={handleSaveGroup}>
                            <h4>{editGroupId != null ? 'Изменить группу' : 'Новая группа'}</h4>
                            <div className="form-group">
                                <label>Название</label>
                                <input
                                    className="form-input"
                                    value={gName}
                                    onChange={e => setGName(e.target.value)}
                                    placeholder="VIP, Скидка 10%, Постоянные…"
                                    required
                                />
                            </div>
                            <div className="form-group">
                                <label>Цвет</label>
                                <div className="groups-color-row">
                                    {GROUP_COLORS.map(c => (
                                        <button
                                            key={c}
                                            type="button"
                                            className={`groups-color-swatch ${gColor === c ? 'active' : ''}`}
                                            style={{ background: c }}
                                            onClick={() => setGColor(c)}
                                            title={c}
                                        />
                                    ))}
                                    <input
                                        type="color"
                                        value={gColor.startsWith('#') && gColor.length === 7 ? gColor : '#6B7280'}
                                        onChange={e => setGColor(e.target.value)}
                                        className="groups-color-picker"
                                    />
                                </div>
                            </div>
                            <div className="form-group">
                                <label>Скидка группы, % (пусто = только метка)</label>
                                <input
                                    className="form-input"
                                    type="number"
                                    min={0}
                                    step={1}
                                    value={gDiscount}
                                    onChange={e => setGDiscount(e.target.value)}
                                    placeholder="необязательно"
                                />
                            </div>
                            {gError && <p className="groups-form-error">{String(gError)}</p>}
                            <div className="groups-form-actions">
                                {editGroupId != null && (
                                    <button type="button" className="btn-search" onClick={resetGroupForm}>Отмена</button>
                                )}
                                <button type="submit" className="btn-submit" disabled={gSaving}>
                                    {gSaving ? 'Сохранение…' : editGroupId != null ? 'Сохранить' : 'Создать'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}
