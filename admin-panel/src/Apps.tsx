import React, { useEffect, useState, useMemo } from "react";
import { getApps, createApp, updateApp, deleteApp, type AppItem } from "./api";
import ImageField from "./ImageField";
import { useClubLive } from "./useClubLive";
import "./Apps.css";

// Жанры для ИГР
const GAME_GENRES = [
    { value: "Shooter", label: "Шутеры" },
    { value: "Racing", label: "Гонки" },
    { value: "RPG", label: "RPG" },
    { value: "Strategy", label: "Стратегии" },
    { value: "Sports", label: "Спорт" },
    { value: "MOBA", label: "MOBA" },
    { value: "Simulation", label: "Симуляторы" },
    { value: "Fighting", label: "Файтинги" },
    { value: "Horror", label: "Хорроры" },
    { value: "Survival", label: "Выживание" },
    { value: "Action", label: "Экшен" },
    { value: "Arcade", label: "Аркады" },
    { value: "Roguelike", label: "Рогалики" },
    { value: "Other", label: "Разное" },
];

// Жанры для ПРИЛОЖЕНИЙ (Новое)
const APP_GENRES = [
    { value: "Browser", label: "Браузеры" },
    { value: "Social", label: "Общение" },
    { value: "Launcher", label: "Лаунчеры" },
    { value: "Tool", label: "Инструменты" },
    { value: "Office", label: "Офис" },
    { value: "Media", label: "Мультимедиа" },
    { value: "System", label: "Система" },
    { value: "Education", label: "Учеба" },
    { value: "Other", label: "Разное" },
];

export default function Apps() {
    const [apps, setApps] = useState<AppItem[]>([]);
    const [loading, setLoading] = useState(true);
    
    // Вкладки: 'Game' или 'Application'
    const [activeTab, setActiveTab] = useState<'Game' | 'Application'>('Game');
    
    // Фильтр по жанру (в списке)
    const [filterGenre, setFilterGenre] = useState<string>('All');

    // Модальное окно
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingItem, setEditingItem] = useState<AppItem | null>(null);

    // Поля формы
    const [title, setTitle] = useState("");
    const [exePath, setExePath] = useState("");
    const [imageUrl, setImageUrl] = useState("");
    const [args, setArgs] = useState("");
    
    // Поле жанра (обновляется при смене типа)
    const [formGenre, setFormGenre] = useState("Shooter"); 

    const fetchApps = async () => {
        setLoading(true);
        try {
            const data = await getApps();
            setApps(data);
        } catch (e) {
            console.error("Ошибка загрузки");
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchApps();
    }, []);

    useClubLive('apps', () => { void fetchApps(); });

    // Определяем текущий список жанров в зависимости от вкладки
    const currentGenreOptions = activeTab === 'Game' ? GAME_GENRES : APP_GENRES;

    // Сброс фильтра при переключении вкладок
    const handleTabChange = (tab: 'Game' | 'Application') => {
        setActiveTab(tab);
        setFilterGenre('All');
        // Ставим дефолтный жанр для формы, чтобы не было путаницы
        setFormGenre(tab === 'Game' ? GAME_GENRES[0].value : APP_GENRES[0].value);
    };

    // 1. ВЫЧИСЛЯЕМ ДОСТУПНЫЕ ЖАНРЫ (Только те, в которых есть программы)
    const availableGenres = useMemo(() => {
        const usedGenres = new Set<string>();
        apps.forEach(app => {
            // Учитываем категорию (игра или софт) и наличие жанра
            const cat = app.category || 'Game';
            if (cat === activeTab && app.genre) {
                usedGenres.add(app.genre);
            }
        });
        return Array.from(usedGenres);
    }, [apps, activeTab]);

    // 2. ФИЛЬТРАЦИЯ СПИСКА
    const filteredApps = apps.filter(app => {
        const cat = app.category || 'Game';
        if (cat !== activeTab) return false;

        if (filterGenre !== 'All') {
            return app.genre === filterGenre;
        }
        return true;
    });

    const openCreateModal = () => {
        setEditingItem(null);
        setTitle("");
        setExePath("");
        setImageUrl("");
        setArgs("");
        // Ставим дефолт в зависимости от вкладки
        setFormGenre(activeTab === 'Game' ? "Shooter" : "Browser"); 
        setIsModalOpen(true);
    };

    const openEditModal = (app: AppItem) => {
        setEditingItem(app);
        setTitle(app.title);
        setExePath(app.exePath);
        setImageUrl(app.imageUrl);
        setArgs(app.arguments || "");
        // Если жанр есть - ставим его, если нет - дефолт
        setFormGenre(app.genre || (activeTab === 'Game' ? "Shooter" : "Browser"));
        setIsModalOpen(true);
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        
        const payload = {
            title,
            exePath,
            imageUrl,
            category: activeTab,
            genre: formGenre, // Теперь отправляем жанр ВСЕГДА (и для игр, и для софта)
            arguments: args
        };

        try {
            if (editingItem) {
                await updateApp(editingItem.id, payload);
            } else {
                await createApp(payload);
            }
            setIsModalOpen(false);
            fetchApps();
        } catch (e) {
            alert("Ошибка сохранения");
        }
    };

    const handleDelete = async (id: number) => {
        if (!confirm("Удалить?")) return;
        try {
            await deleteApp(id);
            fetchApps();
        } catch (e) { alert("Ошибка удаления"); }
    };

    return (
        <div className="page apps-container">
            <div className="page-toolbar apps-header">
                <div className="tabs">
                    <button 
                        className={activeTab === 'Game' ? 'active' : ''} 
                        onClick={() => handleTabChange('Game')}
                    >
                        Игры
                    </button>
                    <button 
                        className={activeTab === 'Application' ? 'active' : ''} 
                        onClick={() => handleTabChange('Application')}
                    >
                        Приложения
                    </button>
                </div>
                <button className="btn-add" onClick={openCreateModal}>+ Добавить</button>
            </div>

            {/* ПАНЕЛЬ ФИЛЬТРОВ (Появляется, если есть хотя бы 1 категория) */}
            {availableGenres.length > 0 && (
                <div className="sub-tabs-container">
                    <button 
                        type="button"
                        className={`sub-tab ${filterGenre === 'All' ? 'active' : ''}`}
                        onClick={() => setFilterGenre('All')}
                    >
                        Все
                    </button>

                    {availableGenres.map(gVal => {
                        const label = currentGenreOptions.find(g => g.value === gVal)?.label || gVal;
                        const isActive = filterGenre === gVal;
                        return (
                            <button 
                                key={gVal}
                                type="button"
                                className={`sub-tab ${isActive ? 'active' : ''}`}
                                onClick={() => setFilterGenre(gVal)}
                            >
                                {label}
                            </button>
                        )
                    })}
                </div>
            )}

            <div className="apps-grid">
                {loading ? (
                    <p className="loading-text">Загрузка...</p>
                ) : filteredApps.length === 0 ? (
                    <div className="empty-state">Список пуст</div>
                ) : (
                    filteredApps.map(app => (
                        <div key={app.id} className="app-card">
                            <div className="app-image-container">
                                {app.imageUrl ? (
                                    <img src={app.imageUrl} alt={app.title} onError={(e) => (e.currentTarget.src = '/no-icon.png')}/>
                                ) : (
                                    <div className="no-image">Нет иконки</div>
                                )}
                            </div>
                            <div className="app-info">
                                <h3>{app.title}</h3>
                                {app.genre && (
                                    <span style={{
                                        fontSize: '10px', 
                                        color: '#FF00FF', 
                                        border: '1px solid rgba(255, 0, 255, 0.2)', 
                                        padding: '2px 8px', 
                                        borderRadius: '10px',
                                        width: 'fit-content',
                                        marginBottom: '6px',
                                        background: 'rgba(255, 0, 255, 0.05)'
                                    }}>
                                        {/* Перевод категории в бейджике */}
                                        {currentGenreOptions.find(g => g.value === app.genre)?.label || app.genre}
                                    </span>
                                )}
                                <p className="path" title={app.exePath}>{app.exePath}</p>
                            </div>
                            <div className="app-actions">
                                <button className="btn-icon edit" onClick={() => openEditModal(app)}>✏️</button>
                                <button className="btn-icon del" onClick={() => handleDelete(app.id)}>🗑</button>
                            </div>
                        </div>
                    ))
                )}
            </div>

            {/* МОДАЛЬНОЕ ОКНО */}
            {isModalOpen && (
                <div className="modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) setIsModalOpen(false) }}>
                    <div className="modal-content">
                        <button className="modal-close" onClick={() => setIsModalOpen(false)}>×</button>
                        <h2>{editingItem ? "Редактировать" : "Добавить"} {activeTab === 'Game' ? "игру" : "приложение"}</h2>
                        
                        <form onSubmit={handleSubmit}>
                            
                            {/* Выбор жанра: меняется список в зависимости от вкладки */}
                            <div className="form-group">
                                <label>Категория</label>
                                <select 
                                    className="form-input"
                                    value={formGenre} 
                                    onChange={e => setFormGenre(e.target.value)}
                                    style={{ height: '42px' }}
                                >
                                    {currentGenreOptions.map(g => (
                                        <option key={g.value} value={g.value}>
                                            {g.label}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            <div className="form-group">
                                <label>Название</label>
                                <input className="form-input" value={title} onChange={e => setTitle(e.target.value)} required autoFocus />
                            </div>
                            
                            <div className="form-group">
                                <label>Путь к EXE</label>
                                <input className="form-input" value={exePath} onChange={e => setExePath(e.target.value)} required placeholder="C:\..." />
                            </div>

                            <div className="form-group">
                                <label>Аргументы запуска</label>
                                <input className="form-input" value={args} onChange={e => setArgs(e.target.value)} placeholder="" />
                            </div>

                            <ImageField
                                label="Обложка"
                                value={imageUrl}
                                onChange={setImageUrl}
                                placeholder="http://..."
                            />

                            <button type="submit" className="btn-submit">
                                {editingItem ? "Сохранить изменения" : "Создать"}
                            </button>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}