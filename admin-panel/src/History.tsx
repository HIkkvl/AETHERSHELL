import React, { useEffect, useState, useMemo } from "react";
import { getLogs, type AdminLog } from "./api";
import "./History.css";

const EVENT_TYPES = [
  { value: "All", label: "Все", icon: "📋" },
  { value: "Session", label: "Сессии", icon: "⏱" },
  { value: "Money", label: "Финансы", icon: "💰" },
  { value: "Shop", label: "Магазин", icon: "🛒" },
  { value: "Power", label: "Питание", icon: "⚡" },
  { value: "Broadcast", label: "Рассылка", icon: "📢" },
  { value: "UserMgmt", label: "Персонал", icon: "👤" },
  { value: "Settings", label: "Настройки", icon: "⚙️" },
];

const DATE_PRESETS = [
  { label: "Сегодня", getValue: () => {
    const today = new Date().toISOString().split('T')[0];
    return { from: today, to: today };
  }},
  { label: "Вчера", getValue: () => {
    const d = new Date();
    d.setDate(d.getDate() - 1);
    const yesterday = d.toISOString().split('T')[0];
    return { from: yesterday, to: yesterday };
  }},
  { label: "7 дней", getValue: () => {
    const to = new Date().toISOString().split('T')[0];
    const d = new Date();
    d.setDate(d.getDate() - 7);
    const from = d.toISOString().split('T')[0];
    return { from, to };
  }},
  { label: "30 дней", getValue: () => {
    const to = new Date().toISOString().split('T')[0];
    const d = new Date();
    d.setDate(d.getDate() - 30);
    const from = d.toISOString().split('T')[0];
    return { from, to };
  }},
];

export default function History() {
  const [logs, setLogs] = useState<AdminLog[]>([]);
  const [loading, setLoading] = useState(true);
  
  // Фильтры
  const [filterType, setFilterType] = useState("All");
  const [searchText, setSearchText] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [adminFilter, setAdminFilter] = useState("All");
  const [activePreset, setActivePreset] = useState<string | null>(null);

  const fetchLogs = async () => {
    setLoading(true);
    try {
      const data = await getLogs(filterType);
      setLogs(data);
    } catch (e) {
      console.error("Ошибка загрузки истории");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchLogs();
  }, [filterType]);

  // Уникальные админы для фильтра
  const uniqueAdmins = useMemo(() => {
    const admins = [...new Set(logs.map(l => l.adminName))];
    return admins.sort();
  }, [logs]);

  // Уникальные цели (ПК) для фильтра
  const uniqueTargets = useMemo(() => {
    const targets = [...new Set(logs.map(l => l.target).filter(Boolean))];
    return targets.sort();
  }, [logs]);

  // Фильтрация на клиенте
  const filteredLogs = useMemo(() => {
    return logs.filter(log => {
      // Поиск по тексту
      if (searchText) {
        const search = searchText.toLowerCase();
        const matchesSearch = 
          log.details?.toLowerCase().includes(search) ||
          log.target?.toLowerCase().includes(search) ||
          log.adminName?.toLowerCase().includes(search);
        if (!matchesSearch) return false;
      }

      // Фильтр по дате
      if (dateFrom) {
        const logDate = new Date(log.createdAt).toISOString().split('T')[0];
        if (logDate < dateFrom) return false;
      }
      if (dateTo) {
        const logDate = new Date(log.createdAt).toISOString().split('T')[0];
        if (logDate > dateTo) return false;
      }

      // Фильтр по админу
      if (adminFilter !== "All" && log.adminName !== adminFilter) {
        return false;
      }

      return true;
    });
  }, [logs, searchText, dateFrom, dateTo, adminFilter]);

  const handleClearFilters = () => {
    setFilterType("All");
    setSearchText("");
    setDateFrom("");
    setDateTo("");
    setAdminFilter("All");
    setActivePreset(null);
  };

  const handleDatePreset = (preset: typeof DATE_PRESETS[0]) => {
    const { from, to } = preset.getValue();
    setDateFrom(from);
    setDateTo(to);
    setActivePreset(preset.label);
  };

  const handleCustomDate = () => {
    setActivePreset(null);
  };

  const getActionStyle = (type: string) => {
    switch(type) {
        case 'Money': return { label: 'Финансы', className: 'badge-money' };
        case 'Power': return { label: 'Питание', className: 'badge-power' };
        case 'Session': return { label: 'Сессия', className: 'badge-session' };
        case 'Shop': return { label: 'Магазин', className: 'badge-shop' };
        case 'Settings': return { label: 'Настройки', className: 'badge-default' };
        case 'UserMgmt': return { label: 'Персонал', className: 'badge-usermgmt' };
        case 'Broadcast': return { label: 'Рассылка', className: 'badge-broadcast' };
        default: return { label: type, className: 'badge-default' };
    }
  };

  const hasActiveFilters = searchText || dateFrom || dateTo || adminFilter !== "All" || filterType !== "All";

  // Активные фильтры для отображения тегов
  const activeFilterTags = useMemo(() => {
    const tags: { label: string; onRemove: () => void }[] = [];
    
    if (filterType !== "All") {
      const type = EVENT_TYPES.find(t => t.value === filterType);
      tags.push({ 
        label: `${type?.icon} ${type?.label}`, 
        onRemove: () => setFilterType("All") 
      });
    }
    if (adminFilter !== "All") {
      tags.push({ 
        label: `👤 ${adminFilter}`, 
        onRemove: () => setAdminFilter("All") 
      });
    }
    if (dateFrom || dateTo) {
      const label = activePreset || `📅 ${dateFrom || '...'} — ${dateTo || '...'}`;
      tags.push({ 
        label, 
        onRemove: () => { setDateFrom(""); setDateTo(""); setActivePreset(null); } 
      });
    }
    if (searchText) {
      tags.push({ 
        label: `"${searchText}"`, 
        onRemove: () => setSearchText("") 
      });
    }
    
    return tags;
  }, [filterType, adminFilter, dateFrom, dateTo, searchText, activePreset]);

  return (
    <div className="page history-container">
      <div className="page-toolbar history-header">
        <p className="page-subtitle">Журнал действий</p>
        <div className="page-actions header-actions">
          <button className="btn-refresh" onClick={fetchLogs} type="button">
            Обновить
          </button>
        </div>
      </div>

      {/* Панель фильтров: поиск слева, фильтр сотрудников справа */}
      <div className="filters-panel">
        <div className="filters-row-search history-search-row">
          <div className="history-search-cell">
            <div className="search-box">
              <span className="search-icon" aria-hidden>⌕</span>
              <input
                type="text"
                placeholder="Поиск по логам..."
                value={searchText}
                onChange={(e) => setSearchText(e.target.value)}
                className="search-input"
                aria-label="Поиск по логам"
              />
              {searchText && (
                <button type="button" className="search-clear" onClick={() => setSearchText("")} aria-label="Очистить">✕</button>
              )}
            </div>
          </div>
          <div className="history-admin-cell">
            <div className="admin-filter-block">
              <label htmlFor="history-admin-filter" className="admin-filter-label">Сотрудник</label>
              <select
                id="history-admin-filter"
                value={adminFilter}
                onChange={(e) => setAdminFilter(e.target.value)}
                className="admin-select admin-select-inline"
                aria-label="Фильтр по сотруднику"
                title="Показать действия выбранного сотрудника"
              >
                <option value="All">Все</option>
                {uniqueAdmins.map(admin => (
                  <option key={admin} value={admin}>{admin}</option>
                ))}
              </select>
            </div>
          </div>
        </div>

        <div className="filters-scroll-label">Тип события</div>
        <div className="filters-scroll-wrap filters-scroll-types">
          <div className="filters-scroll-inner">
            {EVENT_TYPES.map(type => (
              <button
                key={type.value}
                type="button"
                className={`filter-chip ${filterType === type.value ? 'active' : ''}`}
                onClick={() => setFilterType(type.value)}
              >
                <span className="chip-icon">{type.icon}</span>
                <span>{type.label}</span>
              </button>
            ))}
          </div>
        </div>

        <div className="filters-scroll-label">Период</div>
        <div className="filters-scroll-wrap filters-scroll-dates">
          <div className="filters-scroll-inner">
            {DATE_PRESETS.map(preset => (
              <button
                key={preset.label}
                type="button"
                className={`filter-chip filter-chip-date ${activePreset === preset.label ? 'active' : ''}`}
                onClick={() => handleDatePreset(preset)}
              >
                {preset.label}
              </button>
            ))}
            <div className="filter-chip filter-chip-custom">
              <input
                type="date"
                value={dateFrom}
                onChange={(e) => { setDateFrom(e.target.value); handleCustomDate(); }}
                className="date-input-inline"
                title="Дата от"
              />
              <span className="date-sep">—</span>
              <input
                type="date"
                value={dateTo}
                onChange={(e) => { setDateTo(e.target.value); handleCustomDate(); }}
                className="date-input-inline"
                title="Дата до"
              />
            </div>
          </div>
        </div>

        {activeFilterTags.length > 0 && (
          <div className="active-filters">
            {activeFilterTags.map((tag, i) => (
              <span key={i} className="filter-tag">
                {tag.label}
                <button type="button" onClick={tag.onRemove} aria-label="Убрать">✕</button>
              </span>
            ))}
            <button type="button" className="clear-all-btn" onClick={handleClearFilters}>
              Сбросить все
            </button>
          </div>
        )}
      </div>

      <div className="results-info">
        <span className="results-badge">
          <strong>{filteredLogs.length}</strong> записей
        </span>
      </div>

      <div className="history-card">
        {loading ? (
          <div className="loading-state">
            <div className="spinner"></div>
            <span>Загрузка данных...</span>
          </div>
        ) : (
          <table className="logs-table">
            <thead>
              <tr>
                <th style={{width: '160px'}}>Время</th>
                <th style={{width: '120px'}}>Админ</th>
                <th style={{width: '100px'}}>Тип</th>
                <th style={{width: '120px'}}>Объект</th>
                <th>Подробности</th>
              </tr>
            </thead>
            <tbody>
              {filteredLogs.map((log) => {
                const style = getActionStyle(log.actionType);
                return (
                  <tr key={log.id}>
                    <td className="time-col">
                      {new Date(log.createdAt).toLocaleString('ru-RU', {
                        day: '2-digit',
                        month: '2-digit',
                        hour: '2-digit',
                        minute: '2-digit'
                      })}
                    </td>
                    <td>
                      <span 
                        className="admin-link"
                        onClick={() => setAdminFilter(log.adminName)}
                      >
                        {log.adminName}
                      </span>
                    </td>
                    <td>
                      <span 
                        className={`log-badge ${style.className}`}
                        onClick={() => setFilterType(log.actionType)}
                      >
                        {style.label}
                      </span>
                    </td>
                    <td>
                      <span className="target-col">{log.target}</span>
                    </td>
                    <td className="details-col">{log.details}</td>
                  </tr>
                );
              })}
              {filteredLogs.length === 0 && (
                <tr>
                  <td colSpan={5} className="empty-state">
                    {hasActiveFilters ? (
                      <>
                        <p>Ничего не найдено</p>
                        <button onClick={handleClearFilters}>Сбросить фильтры</button>
                      </>
                    ) : (
                      <p>История пуста</p>
                    )}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
