import React, { useEffect, useState } from 'react'; 
import api, { broadcastMessage, shutdownAll, rebootAll, downloadReport } from './api';
import { useClubLive } from './useClubLive';
import './Dashboard.css';

// Интерфейсы соответствуют DTO на сервере
interface DashboardStats {
    totalComputers: number;
    enabledComputers: number;
    disabledComputers: number;
    activeComputers: number;
    errorComputers: number;
    pendingComputers: number;
    usersCount: number;
    appsCount: number;
    topAppCount: number;
    topAppName: string;
    revenueTotal: number;
    revenueKaspi: number;
    revenuePackages: number;
}

interface ErrorPc {
    nameToDisplay: string;
    pcName: string;
    status: string;
    lastSeen: string;
}

interface PendingPc {
    pcName: string;
    isOnline: boolean;
    createdAt: string;
}

interface DashboardProps {
    onNavigateToComputers: () => void;
}

export default function Dashboard({ onNavigateToComputers }: DashboardProps) {
    const [stats, setStats] = useState<DashboardStats | null>(null);
    const [errorPcs, setErrorPcs] = useState<ErrorPc[]>([]);
    const [pendingPcs, setPendingPcs] = useState<PendingPc[]>([]);
    const [loading, setLoading] = useState(true);

    const fetchDashboard = async (silent = false) => {
        if (!silent) setLoading(true);
        try {
            const res = await api.get('/Admin/dashboard');
            setStats(res.data.stats);
            setErrorPcs(res.data.error_pcs || []);
            setPendingPcs(res.data.pending_pcs || []);
        } catch (e) {
            console.error("Ошибка загрузки дашборда", e);
        } finally {
            if (!silent) setLoading(false);
        }
    };

    const handleApprovePc = async (pcName: string) => {
        const displayName = prompt('Введите имя для ПК (например, PC-01):', 'PC-01');
        if (!displayName) return;
        
        try {
            await api.post(`/Admin/approve-computer?pcId=${pcName}&displayName=${encodeURIComponent(displayName)}`);
            fetchDashboard();
        } catch (e) {
            alert('Ошибка подтверждения ПК');
        }
    };

    const handleRejectPc = async (pcName: string) => {
        if (!confirm('Удалить этот ПК из системы?')) return;
        
        try {
            await api.delete(`/Admin/computer?pcId=${pcName}`);
            fetchDashboard();
        } catch (e) {
            alert('Ошибка удаления ПК');
        }
    };

    // === БЫСТРЫЕ ДЕЙСТВИЯ ===

    const handleBroadcast = async () => {
        const message = prompt('Введите сообщение для всех ПК:');
        if (!message) return;

        try {
            const result = await broadcastMessage(message);
            alert(`✅ ${result.message}`);
        } catch (e) {
            alert('Ошибка отправки сообщения');
        }
    };

    const handleShutdownAll = async () => {
        if (!confirm('⚠️ Вы уверены что хотите ВЫКЛЮЧИТЬ ВСЕ компьютеры в зале?')) return;
        if (!confirm('Это действие нельзя отменить! Подтвердите ещё раз.')) return;

        try {
            const result = await shutdownAll();
            alert(`✅ ${result.message}`);
        } catch (e) {
            alert('Ошибка выключения');
        }
    };

    const handleRebootAll = async () => {
        if (!confirm('Перезагрузить ВСЕ компьютеры в зале?')) return;

        try {
            const result = await rebootAll();
            alert(`✅ ${result.message}`);
        } catch (e) {
            alert('Ошибка перезагрузки');
        }
    };

    const handleDownloadReport = async () => {
        const today = new Date().toISOString().split('T')[0];
        const from = prompt('Дата начала (YYYY-MM-DD):', today);
        if (!from) return;
        
        const to = prompt('Дата конца (YYYY-MM-DD):', today);
        if (!to) return;

        try {
            await downloadReport(from, to);
            alert('✅ Отчёт скачан');
        } catch (e) {
            alert('Ошибка скачивания отчёта');
        }
    };

    useEffect(() => {
        fetchDashboard();
        const interval = setInterval(() => fetchDashboard(true), 30000);
        return () => clearInterval(interval);
    }, []);

    useClubLive(['dashboard', 'computers'], () => { void fetchDashboard(true); });

    if (loading) return <div className="dashboard-loading">Загрузка данных...</div>;
    if (!stats) return <div className="dashboard-error">Нет данных</div>;

    // Расчет процентов для прогресс-баров
    const onlinePercent = (stats.enabledComputers / stats.totalComputers) * 100 || 0;
    const revenueKaspiPercent = (stats.revenueKaspi / stats.revenueTotal) * 100 || 0;

    return (
        <div className="page dashboard-container">
            <div className="page-toolbar">
                <p className="page-subtitle">Статистика за сегодня</p>
                <div className="page-actions">
                    <button type="button" className="ui-btn" onClick={() => { void fetchDashboard(); }}>Обновить</button>
                </div>
            </div>

            {/* ОСНОВНЫЕ КАРТОЧКИ */}
            <div className="stats-grid">
                {/* Карточка Выручки */}
                <div className="stat-card revenue-card">
                    <div className="card-icon accent">₸</div>
                    <div className="card-info">
                        <h3>Выручка (День)</h3>
                        <div className="big-number">{stats.revenueTotal.toLocaleString()} ₸</div>
                        <div className="revenue-bars">
                            <div className="bar-group">
                                <div className="bar-label">
                                    <span>Kaspi QR</span>
                                    <span>{stats.revenueKaspi.toLocaleString()} ₸</span>
                                </div>
                                <div className="progress-bg">
                                    <div className="progress-fill kaspi" style={{width: `${revenueKaspiPercent}%`}}></div>
                                </div>
                            </div>
                            <div className="bar-group">
                                <div className="bar-label">
                                    <span>Пакеты</span>
                                    <span>{stats.revenuePackages.toLocaleString()} ₸</span>
                                </div>
                                <div className="progress-bg">
                                    <div className="progress-fill package" style={{width: `${100 - revenueKaspiPercent}%`}}></div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                {/* Карточка Компьютеров */}
                <div className="stat-card pc-card" onClick={onNavigateToComputers} style={{cursor: 'pointer'}}>
                    <div className="card-header-row">
                        <div className="card-icon green">PC</div>
                        <div className="card-badge">{stats.totalComputers} всего</div>
                    </div>
                    <div className="card-info">
                        <h3>Загрузка зала</h3>
                        <div className="big-number">{stats.activeComputers} <span className="small">активных</span></div>
                        <div className="progress-bg large">
                            <div className="progress-fill online" style={{width: `${onlinePercent}%`}}></div>
                        </div>
                        <div className="pc-status-row">
                            <span className="status-item online">{stats.enabledComputers} онлайн</span>
                            {stats.errorComputers > 0 && <span className="status-item error">{stats.errorComputers} ошибок</span>}
                            {stats.pendingComputers > 0 && <span className="status-item pending">{stats.pendingComputers} новых</span>}
                        </div>
                    </div>
                </div>

                {/* Карточка Клиентов */}
                <div className="stat-card users-card">
                    <div className="card-icon blue">CL</div>
                    <div className="card-info">
                        <h3>Клиентская база</h3>
                        <div className="big-number">{stats.usersCount}</div>
                        <p className="card-detail-text">Зарегистрировано пользователей</p>
                    </div>
                </div>

                {/* Карточка Топ Игры */}
                <div className="stat-card game-card">
                    <div className="card-icon amber">TOP</div>
                    <div className="card-info">
                        <h3>Популярное</h3>
                        <div className="game-title">{stats.topAppName}</div>
                        <p className="card-detail-text">Запусков сегодня: <b>{stats.topAppCount}</b></p>
                    </div>
                </div>
            </div>

            {/* НИЖНЯЯ СЕКЦИЯ */}
            <div className="dashboard-bottom-grid">
                {/* Блок с новыми ПК (ожидают подтверждения) */}
                {pendingPcs.length > 0 && (
                    <div className="info-panel pending-panel">
                        <div className="panel-header">
                            <h3>Новые ПК ({pendingPcs.length})</h3>
                        </div>
                        <div className="pending-list">
                            {pendingPcs.map(pc => (
                                <div key={pc.pcName} className="pending-item">
                                    <div className="pc-details">
                                        <div className="pc-name">{pc.pcName.substring(0, 12)}...</div>
                                        <div className="pc-meta">
                                            <span className={`status-dot ${pc.isOnline ? 'online' : 'offline'}`}></span>
                                            {pc.isOnline ? 'В сети' : 'Не в сети'} · {pc.createdAt}
                                        </div>
                                    </div>
                                    <div className="pc-actions">
                                        <button className="btn-approve" onClick={() => handleApprovePc(pc.pcName)}>Подтвердить</button>
                                        <button className="btn-reject" onClick={() => handleRejectPc(pc.pcName)}>Отклонить</button>
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                {/* Блок с ошибками / Offline ПК */}
                <div className="info-panel error-panel">
                    <div className="panel-header">
                        <h3>Требуют внимания ({errorPcs.length})</h3>
                        {errorPcs.length > 0 && <button className="ui-btn ui-btn-sm" onClick={onNavigateToComputers}>Перейти</button>}
                    </div>
                    
                    {errorPcs.length === 0 ? (
                        <div className="empty-state">
                            <span>Все компьютеры в порядке</span>
                        </div>
                    ) : (
                        <div className="error-list">
                            {errorPcs.map(pc => (
                                <div key={pc.pcName} className={`error-item ${pc.status === 'Error' ? 'is-error' : ''}`}>
                                    <div className="pc-details">
                                        <div className="pc-name">{pc.nameToDisplay}</div>
                                        <div className="pc-status">
                                            {pc.status === 'Error' ? 'Ошибка' : 'Оффлайн'}
                                        </div>
                                    </div>
                                    <div className="pc-action">
                                        <span className="last-seen">{pc.lastSeen}</span>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>

                {/* Блок быстрых действий */}
                <div className="info-panel actions-panel">
                    <div className="panel-header"><h3>Быстрые действия</h3></div>
                    <div className="actions-grid">
                        <button className="action-btn" onClick={handleBroadcast}>Сообщение всем</button>
                        <button className="action-btn danger" onClick={handleShutdownAll}>Выключить зал</button>
                        <button className="action-btn" onClick={handleRebootAll}>Перезагрузить зал</button>
                        <button className="action-btn" onClick={handleDownloadReport}>Скачать отчёт</button>
                    </div>
                </div>
            </div>
        </div>
    );
}