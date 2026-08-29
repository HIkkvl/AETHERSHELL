// src/Tariffs.tsx
import React, { useEffect, useState } from "react";
import { getTariffs, createTariff, deleteTariff, updateTariff, type Tariff } from "./api";
import { useClubLive } from "./useClubLive";
import "./Tariffs.css";

export default function Tariffs() {
    const [tariffs, setTariffs] = useState<Tariff[]>([]);
    const [loading, setLoading] = useState(true);
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingTariff, setEditingTariff] = useState<Tariff | null>(null);

    // Form states
    const [name, setName] = useState("");
    const [price, setPrice] = useState(0);
    const [duration, setDuration] = useState(60);
    const [startHour, setStartHour] = useState<string>("");
    const [endHour, setEndHour] = useState<string>("");
    const [feature1, setFeature1] = useState("Vip зона");
    const [feature2, setFeature2] = useState("144Hz монитор");
    
    // Новое состояние для типа тарифа
    const [isFixed, setIsFixed] = useState(false);
    const [isBurnable, setIsBurnable] = useState(false);

    const fetchList = async () => {
        try {
            const data = await getTariffs();
            setTariffs(data);
        } catch (e) {
            console.error(e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { fetchList(); }, []);

    useClubLive('tariffs', () => { void fetchList(); });

    const resetForm = () => {
        setName("");
        setPrice(0);
        setDuration(60);
        setStartHour("");
        setEndHour("");
        setFeature1("Vip зона");
        setFeature2("144Hz монитор");
        setIsFixed(false);
        setIsBurnable(false);
        setEditingTariff(null);
    };

    const openEditModal = (tariff: Tariff) => {
        setEditingTariff(tariff);
        setName(tariff.name);
        setPrice(tariff.price);
        setDuration(tariff.durationMinutes);
        setStartHour(tariff.startHour?.toString() ?? "");
        setEndHour(tariff.endHour?.toString() ?? "");
        setFeature1(tariff.feature1 || "Vip зона");
        setFeature2(tariff.feature2 || "144Hz монитор");
        setIsFixed(tariff.isFixedTime);
        setIsBurnable(!!tariff.isBurnable);
        setIsModalOpen(true);
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const tariffData = {
                name,
                price,
                durationMinutes: isFixed ? 0 : duration, 
                startHour: startHour !== "" ? Number(startHour) : undefined,
                endHour: endHour !== "" ? Number(endHour) : undefined,
                isFixedTime: isFixed,
                isBurnable,
                feature1,
                feature2
            };

            if (editingTariff) {
                await updateTariff(editingTariff.id, tariffData as Tariff);
            } else {
                await createTariff(tariffData as Omit<Tariff, 'id'>);
            }

            resetForm();
            setIsModalOpen(false);
            fetchList();
        } catch (e) {
            alert("Ошибка сохранения тарифа");
        }
    };

    const handleDelete = async (id: number) => {
        if (!confirm("Удалить этот тариф?")) return;
        await deleteTariff(id);
        fetchList();
    };

    const formatInfo = (t: Tariff) => {
        if (t.isFixedTime) {
            return (
                <span className="mode-badge time-bound" style={{background: 'linear-gradient(135deg, #8b5cf6, #6d28d9)', color: '#fff'}}>
                     🌙 Пакет до {t.endHour}:00
                </span>
            );
        }
        return <span style={{color:'var(--text-secondary)'}}>{t.durationMinutes} мин</span>;
    };

    return (
        <div className="page tariffs-container">
            <div className="page-toolbar tariffs-header-row">
                <p className="page-subtitle">Тарифы клуба</p>
                <div className="page-actions">
                    <button className="btn-plus-round" onClick={() => setIsModalOpen(true)}>+</button>
                </div>
            </div>

            <div className="tariffs-list-card">
                {loading ? <p>Загрузка...</p> : (
                    <table>
                        <thead>
                            <tr>
                                <th>Название</th>
                                <th>Тип / Длительность</th>
                                <th>Сгораемость</th>
                                <th>Условие покупки</th>
                                <th>Цена</th>
                                <th style={{ textAlign: 'center' }}>Действия</th>
                            </tr>
                        </thead>
                        <tbody>
                            {tariffs.map((t) => (
                                <tr key={t.id}>
                                    <td className="tariff-name">{t.name}</td>
                                    <td>{formatInfo(t)}</td>
                                    <td>
                                        {t.isBurnable ? (
                                            <span className="mode-badge" style={{background: 'var(--red-soft, #fde8e8)', color: 'var(--red)'}}>Сгораемый</span>
                                        ) : (
                                            <span className="mode-badge" style={{background: 'var(--green-soft, #e8f7ee)', color: 'var(--green)'}}>Несгораемый</span>
                                        )}
                                    </td>
                                    <td>
                                        {(t.startHour !== undefined && t.startHour !== null) 
                                            ? `с ${t.startHour}:00` 
                                            : "Круглосуточно"}
                                    </td>
                                    <td className="tariff-price">{t.price} ₸</td>
                                    <td style={{ textAlign: 'center' }}>
                                        <button className="btn-edit" onClick={() => openEditModal(t)} title="Редактировать">✏️</button>
                                        <button className="btn-delete" onClick={() => handleDelete(t.id)} title="Удалить">🗑️</button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>

            {isModalOpen && (
                <div className="modal-overlay" onClick={(e) => { if(e.target === e.currentTarget) { setIsModalOpen(false); resetForm(); } }}>
                    <div className="modal-content">
                        <button className="modal-close" onClick={() => { setIsModalOpen(false); resetForm(); }}>×</button>
                        <h3 className="modal-title">{editingTariff ? 'Редактировать тариф' : 'Создать тариф'}</h3>
                        
                        <form onSubmit={handleSubmit}>
                            {/* ПЕРЕКЛЮЧАТЕЛЬ ТИПА */}
                            <div className="form-group toggle-group" style={{flexDirection: 'row', alignItems: 'center', gap: '10px', marginBottom: '20px'}}>
                                <label className="switch">
                                    <input type="checkbox" checked={isFixed} onChange={e => setIsFixed(e.target.checked)} />
                                    <span className="slider round"></span>
                                </label>
                                <span>{isFixed ? "Фиксированный пакет (до времени)" : "Обычный (по минутам)"}</span>
                            </div>

                            <div className="form-group toggle-group" style={{flexDirection: 'row', alignItems: 'center', gap: '10px', marginBottom: '20px'}}>
                                <label className="switch">
                                    <input type="checkbox" checked={isBurnable} onChange={e => setIsBurnable(e.target.checked)} />
                                    <span className="slider round"></span>
                                </label>
                                <span>{isBurnable ? "Сгораемый (остаток не сохраняется)" : "Несгораемый (остаток в профиле)"}</span>
                            </div>

                            <div className="form-group">
                                <label>Название</label>
                                <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Название..." required />
                            </div>

                            <div className="form-group">
                                <label>Цена (₸)</label>
                                <input type="number" value={price} onChange={(e) => setPrice(Number(e.target.value))} required />
                            </div>

                            {!isFixed && (
                                <div className="form-group">
                                    <label>Длительность (минут)</label>
                                    <input type="number" value={duration} onChange={(e) => setDuration(Number(e.target.value))} required />
                                </div>
                            )}

                            <div style={{ display: "flex", gap: "15px" }}>
                                <div className="form-group" style={{ flex: 1 }}>
                                    <label>{isFixed ? "Начало действия (час)" : "Разрешить покупку С (час)"}</label>
                                    <input type="number" value={startHour} onChange={(e) => setStartHour(e.target.value)} placeholder="22" />
                                </div>
                                <div className="form-group" style={{ flex: 1 }}>
                                    <label>{isFixed ? "Конец действия (час)" : "Разрешить покупку ДО (час)"}</label>
                                    <input type="number" value={endHour} onChange={(e) => setEndHour(e.target.value)} placeholder="08" required={isFixed} />
                                </div>
                            </div>
                            
                            {isFixed && <small className="hint" style={{color: 'var(--accent)'}}>Клиент будет играть строго до указанного часа "Конец действия".</small>}

                            <div style={{ display: "flex", gap: "15px", marginTop: '15px' }}>
                                <div className="form-group" style={{ flex: 1 }}>
                                    <label>Фича 1 (текст на карточке)</label>
                                    <input value={feature1} onChange={(e) => setFeature1(e.target.value)} placeholder="Vip зона" />
                                </div>
                                <div className="form-group" style={{ flex: 1 }}>
                                    <label>Фича 2 (текст на карточке)</label>
                                    <input value={feature2} onChange={(e) => setFeature2(e.target.value)} placeholder="144Hz монитор" />
                                </div>
                            </div>

                            <button type="submit" className="btn-primary" style={{marginTop: '15px'}}>{editingTariff ? 'Сохранить' : 'Создать'}</button>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}