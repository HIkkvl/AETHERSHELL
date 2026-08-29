import { useEffect, useState } from "react";
import { getBanners, createBanner, updateBanner, deleteBanner, type Banner } from "./api";
import ImageField from "./ImageField";
import { useClubLive } from "./useClubLive";
import "./Banners.css";

export default function Banners() {
  const [banners, setBanners] = useState<Banner[]>([]);
  const [loading, setLoading] = useState(true);
  const [isModalOpen, setIsModalOpen] = useState(false);

  // Form State
  const [editId, setEditId] = useState<number | null>(null);
  const [title, setTitle] = useState("");
  const [imageUrl, setImageUrl] = useState("");
  const [clickUrl, setClickUrl] = useState("");
  const [position, setPosition] = useState(1);
  const [isActive, setIsActive] = useState(true);

  const fetchList = async () => {
    try {
      const data = await getBanners(false);
      setBanners(data);
    } catch (e) {
      console.error("Error loading banners");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchList();
  }, []);

  useClubLive('banners', () => { void fetchList(); });

  const openModal = (banner?: Banner) => {
    if (banner) {
        setEditId(banner.id);
        setTitle(banner.title);
        setImageUrl(banner.imageUrl);
        setClickUrl(banner.clickUrl);
        setPosition(banner.position);
        setIsActive(banner.isActive);
    } else {
        setEditId(null);
        setTitle("");
        setImageUrl("");
        setClickUrl("");
        setPosition(1);
        setIsActive(true);
    }
    setIsModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const payload = { title, imageUrl, clickUrl, position, isActive };
      if (editId !== null) {
        await updateBanner(editId, payload);
      } else {
        await createBanner(payload);
      }
      setIsModalOpen(false);
      fetchList();
    } catch (e: any) {
      alert("Ошибка: " + (e.response?.data?.message || "Не удалось сохранить"));
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm("Удалить этот баннер?")) return;
    try {
      await deleteBanner(id);
      fetchList();
    } catch (e) { alert("Ошибка удаления"); }
  };

  const handleToggleActive = async (banner: Banner) => {
    try {
      await updateBanner(banner.id, { isActive: !banner.isActive });
      fetchList();
    } catch (e) { alert("Ошибка обновления статуса"); }
  };

  return (
    <div className="page banners-container">
      <div className="page-toolbar banners-header-row">
        <p className="page-subtitle">Рекламные баннеры</p>
        <div className="page-actions">
          <button 
              className="btn-plus-round" 
              onClick={() => openModal()} 
              title="Добавить баннер"
          >
              +
          </button>
        </div>
      </div>

      {/* СПИСОК */}
      <div className="banners-list-card">
          {loading ? (
            <p style={{padding:'20px', color:'var(--text-secondary)'}}>Загрузка...</p>
          ) : banners.length === 0 ? (
            <div style={{padding:'40px', textAlign:'center', color:'var(--text-secondary)'}}>
                Список пуст. Добавьте первый баннер.
            </div>
          ) : (
            <table className="banners-table">
              <thead>
                <tr>
                  <th style={{width: '80px'}}>Превью</th>
                  <th>Позиция</th>
                  <th>Название</th>
                  <th>Ссылка</th>
                  <th>Статус</th>
                  <th>Действия</th>
                </tr>
              </thead>
              <tbody>
                {banners.map((b) => (
                  <tr key={b.id}>
                    <td>
                      <img src={b.imageUrl} className="banner-preview-mini" alt="" onError={(e)=>e.currentTarget.style.display='none'}/>
                    </td>
                    <td>
                      <span className={`pos-badge ${b.position === 1 ? 'left' : 'right'}`}>
                        {b.position === 1 ? 'ЛЕВЫЙ' : 'ПРАВЫЙ'}
                      </span>
                    </td>
                    <td style={{fontWeight: 600}}>{b.title}</td>
                    <td style={{fontSize:'12px', color:'var(--accent-blue)'}}>
                        {b.clickUrl ? (b.clickUrl.length > 20 ? b.clickUrl.slice(0,20)+'...' : b.clickUrl) : '-'}
                    </td>
                    <td>
                      <button 
                        className={`status-btn ${b.isActive ? 'active' : 'inactive'}`}
                        onClick={() => handleToggleActive(b)}
                      >
                        {b.isActive ? "ВКЛ" : "ВЫКЛ"}
                      </button>
                    </td>
                    <td>
                        <div className="actions-cell">
                            <button className="btn-icon" title="Изменить" onClick={() => openModal(b)}>✏️</button>
                            <button className="btn-icon del" title="Удалить" onClick={() => handleDelete(b.id)}>🗑</button>
                        </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </div>

      {/* МОДАЛЬНОЕ ОКНО */}
      {isModalOpen && (
        <div className="modal-overlay" onClick={(e) => { if(e.target === e.currentTarget) setIsModalOpen(false) }}>
            <div className="modal-content">
                <button className="modal-close" onClick={() => setIsModalOpen(false)}>×</button>
                <h3 className="modal-title">{editId ? "Редактирование" : "Новый баннер"}</h3>
                
                <form onSubmit={handleSubmit}>
                    <div className="form-group">
                        <label>Название (для админа)</label>
                        <input 
                            className="form-input"
                            type="text" 
                            placeholder="Например: Promo CS2" 
                            value={title} 
                            onChange={e => setTitle(e.target.value)} 
                            required 
                            autoFocus
                        />
                    </div>

                    <div className="form-group">
                        <label>Позиция на экране</label>
                        <select 
                            className="form-input"
                            value={position} 
                            onChange={e => setPosition(Number(e.target.value))}
                        >
                            <option value={1}>Слева (Вертикальный)</option>
                            <option value={2}>Справа (Вертикальный)</option>
                        </select>
                    </div>

                    <ImageField
                        label="Картинка баннера"
                        value={imageUrl}
                        onChange={setImageUrl}
                        required
                    />

                    <div className="form-group">
                        <label>URL Клике (Куда переходить)</label>
                        <input 
                            className="form-input"
                            type="url" 
                            placeholder="https://site.com" 
                            value={clickUrl} 
                            onChange={e => setClickUrl(e.target.value)} 
                        />
                    </div>

                    <div className="form-group">
                        <label className="checkbox-label">
                            <input 
                                type="checkbox" 
                                checked={isActive} 
                                onChange={e => setIsActive(e.target.checked)} 
                            />
                            <span>Активен</span>
                        </label>
                    </div>

                    <button type="submit" className="btn-submit">
                        {editId ? "Сохранить" : "Создать"}
                    </button>
                </form>
            </div>
        </div>
      )}
    </div>
  );
}