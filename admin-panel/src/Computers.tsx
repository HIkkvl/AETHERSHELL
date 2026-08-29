import React, { useEffect, useState, useMemo } from "react";
import api, { shutdownPc, rebootPc, stopSession, startSession, renamePc, deletePc, getComputerDetails } from "./api";
import type { ComputerDetails } from "./api";
import { useClubLive } from "./useClubLive";
import "./Computers.css";

interface Computer {
    id: number;
    pcName: string;
    nameToDisplay: string;
    groupName: string;
    isOnline: boolean;
    currentUser: string | null;
    sessionEndTime: string | null;
    currentApp: string | null;
    currentAppTitle: string | null;
    currentAppSince: string | null;
}

type ComputerGroup = 'Common' | 'VIP';

/// Сколько времени клиент уже в этом приложении, в виде «1 ч 20 мин».
function formatAppDuration(since: string | null): string | null {
    if (!since) return null;

    const started = new Date(since).getTime();
    if (Number.isNaN(started)) return null;

    const minutes = Math.floor((Date.now() - started) / 60000);
    if (minutes < 1) return 'только что';
    if (minutes < 60) return `${minutes} мин`;

    const hours = Math.floor(minutes / 60);
    const rest = minutes % 60;
    return rest === 0 ? `${hours} ч` : `${hours} ч ${rest} мин`;
}

export default function Computers() {
    const [computers, setComputers] = useState<Computer[]>([]);
    const [loading, setLoading] = useState(true);
    
    // Состояние для переключения вкладок
    const [activeTab, setActiveTab] = useState<ComputerGroup>('Common');
    
    // Состояние для модального окна с информацией о ПК
    const [selectedPcDetails, setSelectedPcDetails] = useState<ComputerDetails | null>(null);
    const [detailsLoading, setDetailsLoading] = useState(false);

    const fetchComputers = async () => {
        try {
            const res = await api.get("/Admin/computers");
            setComputers(res.data);
        } catch (e) {
            console.error("Ошибка загрузки ПК", e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchComputers();
        // Backup-poll: основной поток — SignalR DashboardUpdate / ComputersUpdated
        const interval = setInterval(fetchComputers, 15000);
        return () => clearInterval(interval);
    }, []);

    useClubLive(['computers', 'dashboard'], fetchComputers);

    // --- Actions ---

    const handleStop = async (pcName: string) => {
        if (!confirm(`Завершить сессию на ${pcName}?`)) return;
        await stopSession(pcName);
        fetchComputers();
    };

    const handleAddMinutes = async (pcName: string) => {
        const min = prompt("На сколько минут открыть (бесплатно)?", "60");
        if (!min) return;
        await startSession(pcName, Number(min));
        fetchComputers();
    };

    const handleShutdown = async (pcName: string) => {
        if (!confirm(`Выключить ${pcName}?`)) return;
        await shutdownPc(pcName);
    };

    const handleReboot = async (pcName: string) => {
        if (!confirm(`Перезагрузить ${pcName}?`)) return;
        await rebootPc(pcName);
    };

    const handleRename = async (pcName: string, currentName: string) => {
        const newName = prompt("Введите новое имя ПК:", currentName);
        if (!newName || newName === currentName) return;
        try {
            await renamePc(pcName, newName);
            fetchComputers();
        } catch (e) {
            alert("Ошибка переименования");
        }
    };

    const handleDelete = async (pcName: string, displayName: string) => {
        if (!confirm(`Удалить компьютер "${displayName}"?\n\nЭто действие нельзя отменить.`)) return;
        try {
            await deletePc(pcName);
            fetchComputers();
        } catch (e) {
            alert("Ошибка удаления компьютера");
        }
    };

    // Загрузка детальной информации о ПК (клик по карточке)
    const handleShowDetails = async (pcName: string) => {
        setDetailsLoading(true);
        setSelectedPcDetails(null); // Сначала открываем модалку с загрузкой
        try {
            const details = await getComputerDetails(pcName);
            setSelectedPcDetails(details);
        } catch (err) {
            console.error("Ошибка загрузки информации о ПК", err);
            setSelectedPcDetails(null);
        } finally {
            setDetailsLoading(false);
        }
    };

    // Парсинг информации о дисках
    const parseDiskInfo = (diskInfo: string | null) => {
        if (!diskInfo) return [];
        try {
            return JSON.parse(diskInfo) as Array<{ Name: string; TotalGb: number; FreeGb: number; UsedPercent: number }>;
        } catch {
            return [];
        }
    };

    // Форматирование RAM
    const formatRam = (totalMb: number | null, usedMb: number | null) => {
        if (!totalMb) return "N/A";
        const totalGb = (totalMb / 1024).toFixed(1);
        const usedGb = usedMb ? (usedMb / 1024).toFixed(1) : "0";
        const percent = usedMb ? Math.round((usedMb / totalMb) * 100) : 0;
        return `${usedGb} / ${totalGb} ГБ (${percent}%)`;
    };

    const getStatusClass = (pc: Computer) => {
        if (!pc.isOnline) return "status-offline";
        if (pc.currentUser) return "status-busy";
        return "status-free";
    };

    // Фильтрация списка в зависимости от выбранной вкладки
    const filteredComputers = useMemo(() => {
        return computers.filter(pc => {
            if (activeTab === 'VIP') {
                return pc.groupName === 'VIP Комната' || pc.nameToDisplay.toUpperCase().includes('VIP');
            } else {
                return pc.groupName !== 'VIP Комната' && !pc.nameToDisplay.toUpperCase().includes('VIP');
            }
        });
    }, [computers, activeTab]);

    return (
        <div className="page computers-container">
            <div className="page-toolbar computers-header">
                <div className="group-tabs ui-tabs" style={{ marginBottom: 0 }}>
                    <button
                        type="button"
                        className={`tab-btn ui-tab ${activeTab === 'Common' ? 'active' : ''}`}
                        onClick={() => setActiveTab('Common')}
                    >
                        Общий зал
                    </button>
                    <button
                        type="button"
                        className={`tab-btn ui-tab ${activeTab === 'VIP' ? 'active' : ''}`}
                        onClick={() => setActiveTab('VIP')}
                    >
                        VIP
                    </button>
                </div>
                <p className="page-subtitle">ПК в списке: {filteredComputers.length}</p>
            </div>

            {loading ? (
                <p>Загрузка данных...</p>
            ) : (
                <div className="grid-layout">
                    {filteredComputers.length === 0 && (
                        <div style={{gridColumn: '1/-1', textAlign: 'center', padding: '40px', color: 'var(--text-secondary)'}}>
                            В этом зале нет компьютеров
                        </div>
                    )}

                    {filteredComputers.map((pc) => {
                        const statusClass = getStatusClass(pc);
                        const isOnline = pc.isOnline;
                        const isBusy = !!pc.currentUser;
                        const appDuration = formatAppDuration(pc.currentAppSince);

                        return (
                            <div 
                                key={pc.id} 
                                className={`pc-card ${statusClass}`}
                                onClick={() => handleShowDetails(pc.pcName)}
                                style={{ cursor: 'pointer' }}
                                title="Нажмите для просмотра информации о ПК"
                            >
                                {/* HEADER */}
                                <div className="pc-header">
                                    <div>
                                        <div className="pc-title-row">
                                            <h3 className="pc-name">{pc.nameToDisplay}</h3>
                                            <button
                                                className="btn-edit-name"
                                                title="Переименовать"
                                                onClick={(e) => { e.stopPropagation(); handleRename(pc.pcName, pc.nameToDisplay); }}
                                            >
                                                ✏️
                                            </button>
                                        </div>
                                        <span className="pc-group-badge">{pc.groupName || 'Общий зал'}</span>
                                    </div>
                                </div>

                                {/* STATUS BADGE */}
                                <div className="status-badge">
                                    {!isOnline && <>⚫ ОФФЛАЙН</>}
                                    {isOnline && !isBusy && <>🟢 СВОБОДЕН</>}
                                    {isOnline && isBusy && (
                                        pc.currentApp
                                            ? <>🎮 {pc.currentApp}</>
                                            : <>🔵 В ИГРЕ</>
                                    )}
                                </div>

                                {/* INFO BODY */}
                                <div className="pc-info">
                                    {!isOnline && (
                                        <p className="info-message">Нет связи с клиентом</p>
                                    )}

                                    {isOnline && !isBusy && (
                                        <p className="info-message" style={{color: 'var(--accent-green)'}}>
                                            Готов к работе
                                        </p>
                                    )}

                                    {isOnline && isBusy && (
                                        <div className="session-info">
                                            <span className="user-label">Игрок:</span>
                                            <span className="user-value">{pc.currentUser}</span>
                                            {pc.sessionEndTime && (
                                                <span className="time-left">
                                                    До: {new Date(pc.sessionEndTime).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                                                </span>
                                            )}
                                            {pc.currentApp && (
                                                <span className="current-app" title={pc.currentAppTitle || pc.currentApp}>
                                                    {pc.currentApp}{appDuration ? ` · ${appDuration}` : ''}
                                                </span>
                                            )}
                                        </div>
                                    )}
                                </div>

                                {/* ACTIONS FOOTER */}
                                <div className="pc-actions" onClick={(e) => e.stopPropagation()}>
                                    {isOnline ? (
                                        isBusy ? (
                                            <button className="btn-main stop" onClick={() => handleStop(pc.pcName)}>
                                                Завершить
                                            </button>
                                        ) : (
                                            <button className="btn-main start" onClick={() => handleAddMinutes(pc.pcName)}>
                                                Открыть
                                            </button>
                                        )
                                    ) : (
                                        <div /> 
                                    )}

                                    <div className="power-controls">
                                        <button 
                                            className="btn-icon" 
                                            title="Перезагрузка" 
                                            onClick={() => handleReboot(pc.pcName)}
                                        >
                                            🔄
                                        </button>
                                        <button 
                                            className="btn-icon danger" 
                                            title="Выключение" 
                                            onClick={() => handleShutdown(pc.pcName)}
                                        >
                                            🔌
                                        </button>
                                        <button 
                                            className="btn-icon delete" 
                                            title="Удалить компьютер" 
                                            onClick={() => handleDelete(pc.pcName, pc.nameToDisplay)}
                                        >
                                            🗑️
                                        </button>
                                    </div>
                                </div>
                            </div>
                        );
                    })}
                </div>
            )}

            {/* Модальное окно с информацией о ПК */}
            {(selectedPcDetails || detailsLoading) && (
                <div className="modal-overlay" onClick={() => { setSelectedPcDetails(null); setDetailsLoading(false); }}>
                    <div className="pc-details-modal" onClick={(e) => e.stopPropagation()}>
                        <button className="modal-close" onClick={() => { setSelectedPcDetails(null); setDetailsLoading(false); }}>×</button>
                        
                        {selectedPcDetails && (
                        <div className="modal-header">
                            <h2>📊 {selectedPcDetails.displayName}</h2>
                            <span className={`status-tag ${selectedPcDetails.isOnline ? 'online' : 'offline'}`}>
                                {selectedPcDetails.isOnline ? '🟢 Онлайн' : '⚫ Оффлайн'}
                            </span>
                        </div>
                        )}

                        {detailsLoading ? (
                            <div className="loading-spinner">Загрузка...</div>
                        ) : selectedPcDetails ? (
                            <div className="details-grid">
                                {/* Что запущено сейчас */}
                                <div className="details-section full-width">
                                    <h3>🎮 Запущено сейчас</h3>
                                    {selectedPcDetails.currentApp ? (
                                        <>
                                            <div className="detail-row">
                                                <span className="label">Приложение:</span>
                                                <span className="value">{selectedPcDetails.currentApp}</span>
                                            </div>
                                            {selectedPcDetails.currentAppTitle && (
                                                <div className="detail-row">
                                                    <span className="label">Окно:</span>
                                                    <span className="value">{selectedPcDetails.currentAppTitle}</span>
                                                </div>
                                            )}
                                            <div className="detail-row">
                                                <span className="label">Запущено:</span>
                                                <span className="value">
                                                    {formatAppDuration(selectedPcDetails.currentAppSince) || 'N/A'}
                                                </span>
                                            </div>
                                        </>
                                    ) : (
                                        <div className="detail-row">
                                            <span className="value">Ничего не запущено</span>
                                        </div>
                                    )}
                                </div>

                                {/* Сетевая информация */}
                                <div className="details-section">
                                    <h3>🌐 Сеть</h3>
                                    <div className="detail-row">
                                        <span className="label">IP адрес:</span>
                                        <span className="value">{selectedPcDetails.ipAddress || 'N/A'}</span>
                                    </div>
                                    <div className="detail-row">
                                        <span className="label">MAC адрес:</span>
                                        <span className="value mono">{selectedPcDetails.macAddress || 'N/A'}</span>
                                    </div>
                                </div>

                                {/* Процессор */}
                                <div className="details-section">
                                    <h3>🖥️ Процессор</h3>
                                    <div className="detail-row">
                                        <span className="value cpu-name">{selectedPcDetails.cpuName || 'N/A'}</span>
                                    </div>
                                </div>

                                {/* Видеокарта */}
                                <div className="details-section">
                                    <h3>🎮 Видеокарта</h3>
                                    <div className="detail-row">
                                        <span className="value gpu-name">{selectedPcDetails.gpuName || 'N/A'}</span>
                                    </div>
                                </div>

                                {/* Оперативная память */}
                                <div className="details-section">
                                    <h3>💾 Оперативная память</h3>
                                    <div className="detail-row">
                                        <span className="value">{formatRam(selectedPcDetails.ramTotalMb, selectedPcDetails.ramUsedMb)}</span>
                                    </div>
                                    {selectedPcDetails.ramTotalMb && selectedPcDetails.ramUsedMb && (
                                        <div className="progress-bar">
                                            <div 
                                                className="progress-fill ram" 
                                                style={{ width: `${(selectedPcDetails.ramUsedMb / selectedPcDetails.ramTotalMb) * 100}%` }}
                                            />
                                        </div>
                                    )}
                                </div>

                                {/* Диски */}
                                <div className="details-section full-width">
                                    <h3>💿 Накопители</h3>
                                    {parseDiskInfo(selectedPcDetails.diskInfo).length > 0 ? (
                                        <div className="disks-grid">
                                            {parseDiskInfo(selectedPcDetails.diskInfo).map((disk, idx) => (
                                                <div key={idx} className="disk-item">
                                                    <div className="disk-header">
                                                        <span className="disk-name">{disk.Name}</span>
                                                        <span className="disk-usage">{disk.TotalGb - disk.FreeGb} / {disk.TotalGb} ГБ</span>
                                                    </div>
                                                    <div className="progress-bar">
                                                        <div 
                                                            className={`progress-fill disk ${disk.UsedPercent > 90 ? 'critical' : disk.UsedPercent > 70 ? 'warning' : ''}`}
                                                            style={{ width: `${disk.UsedPercent}%` }}
                                                        />
                                                    </div>
                                                    <span className="disk-percent">{disk.UsedPercent}% занято</span>
                                                </div>
                                            ))}
                                        </div>
                                    ) : (
                                        <span className="no-data">Нет данных</span>
                                    )}
                                </div>

                                {/* ОС */}
                                <div className="details-section">
                                    <h3>🪟 Операционная система</h3>
                                    <div className="detail-row">
                                        <span className="value">{selectedPcDetails.osVersion || 'N/A'}</span>
                                    </div>
                                </div>

                                {/* Последнее обновление */}
                                {selectedPcDetails.systemInfoUpdatedAt && (
                                    <div className="details-section">
                                        <h3>🕐 Обновлено</h3>
                                        <div className="detail-row">
                                            <span className="value">
                                                {new Date(selectedPcDetails.systemInfoUpdatedAt).toLocaleString()}
                                            </span>
                                        </div>
                                    </div>
                                )}
                            </div>
                        ) : (
                            <div className="loading-spinner">Ошибка загрузки</div>
                        )}
                    </div>
                </div>
            )}
        </div>
    );
}