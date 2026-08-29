import React, { useEffect, useState, useMemo } from "react";
import { getProducts, createProduct, deleteProduct, type Product } from "./api";
import ImageField from "./ImageField";
import { useClubLive } from "./useClubLive";
import "./Products.css";

export default function Products() {
  const [products, setProducts] = useState<Product[]>([]);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [activeTab, setActiveTab] = useState("Все");
  
  // Форма добавления
  const [name, setName] = useState("");
  const [price, setPrice] = useState("");
  const [category, setCategory] = useState("Напитки");
  const [image, setImage] = useState("");

  const fetchProducts = async () => {
    try {
      const data = await getProducts();
      setProducts(data);
    } catch (e) {
      console.error("Ошибка загрузки меню");
    }
  };

  useEffect(() => {
    fetchProducts();
  }, []);

  useClubLive('products', () => { void fetchProducts(); });

  const filteredProducts = useMemo(() => {
    if (activeTab === "Все") return products;
    return products.filter(p => p.category === activeTab);
  }, [products, activeTab]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name || !price) return;

    try {
        await createProduct({
            name,
            category,
            price: Number(price),
            imageUrl: image || "https://placehold.co/100?text=NO+IMG"
        });
        
        setName("");
        setPrice("");
        setImage("");
        setIsModalOpen(false);
        fetchProducts();
    } catch (e) {
        alert("Ошибка добавления");
    }
  };

  const handleDelete = async (id: number) => {
    if(!confirm("Убрать товар из меню?")) return;
    await deleteProduct(id);
    fetchProducts();
  };

  const categories = ["Все", "Напитки", "Еда", "Снеки", "Комбо", "Прочее"];

  return (
    <div className="page products-container">
      <div className="page-toolbar products-header-row">
        <p className="page-subtitle">Меню бара и кухни</p>
        <div className="page-actions">
          <button 
              className="btn-plus-round" 
              onClick={() => setIsModalOpen(true)}
              title="Добавить товар"
          >
              +
          </button>
        </div>
      </div>

      {/* 2. ТАБЫ */}
      <div className="tabs-container">
          <div className="category-tabs">
                {categories.map(cat => (
                    <button 
                        key={cat}
                        className={`tab-btn ${activeTab === cat ? 'active' : ''}`}
                        onClick={() => setActiveTab(cat)}
                    >
                        {cat}
                    </button>
                ))}
          </div>
      </div>

      {/* 3. ТАБЛИЦА */}
      <div className="list-card">
        <table className="products-table">
            <thead>
                <tr>
                    <th style={{width: '60px'}}>Фото</th>
                    <th>Наименование</th>
                    <th>Категория</th>
                    <th>Цена</th>
                    <th style={{textAlign: 'right'}}>Действие</th>
                </tr>
            </thead>
            <tbody>
                {filteredProducts.map(p => (
                    <tr key={p.id}>
                        <td>
                            <img src={p.imageUrl} alt="" className="prod-img"/>
                        </td>
                        <td style={{fontWeight: 600}}>{p.name}</td>
                        {/* УБРАЛИ ЛИШНИЙ DIV, ПЕРЕИМЕНОВАЛИ КЛАСС */}
                        <td>
                            <span className="cat-badge">{p.category}</span>
                        </td>
                        <td className="price">{p.price} ₸</td>
                        <td style={{textAlign: 'right'}}>
                            <div style={{display:'flex', justifyContent:'flex-end'}}>
                                <button className="btn-del" title="Удалить" onClick={() => handleDelete(p.id)}>
                                    🗑
                                </button>
                            </div>
                        </td>
                    </tr>
                ))}
                {filteredProducts.length === 0 && (
                    <tr>
                        <td colSpan={5} style={{textAlign: 'center', padding: '40px', color: 'var(--text-secondary)'}}>
                            В этой категории пока пусто.
                        </td>
                    </tr>
                )}
            </tbody>
        </table>
      </div>

      {/* 4. МОДАЛКА */}
      {isModalOpen && (
        <div className="modal-overlay" onClick={(e) => { if(e.target === e.currentTarget) setIsModalOpen(false) }}>
            <div className="modal-content">
                <button className="modal-close" onClick={() => setIsModalOpen(false)}>×</button>
                <h3 className="modal-title">Добавить товар</h3>
                
                <form onSubmit={handleCreate}>
                    <div className="form-group">
                        <label>Название товара</label>
                        <input 
                            value={name} 
                            onChange={e => setName(e.target.value)} 
                            placeholder="Например: Coca-Cola 0.5" 
                            autoFocus
                        />
                    </div>
                    <div className="form-group">
                        <label>Стоимость (₸)</label>
                        <input 
                            type="number" 
                            value={price} 
                            onChange={e => setPrice(e.target.value)} 
                            placeholder="0" 
                        />
                    </div>
                    <div className="form-group">
                        <label>Категория</label>
                        <select value={category} onChange={e => setCategory(e.target.value)}>
                            <option>Напитки</option>
                            <option>Еда</option>
                            <option>Снеки</option>
                            <option>Комбо</option>
                            <option>Прочее</option>
                        </select>
                    </div>
                    <ImageField
                        label="Изображение"
                        value={image}
                        onChange={setImage}
                    />
                    <button type="submit" className="btn-submit">Сохранить</button>
                </form>
            </div>
        </div>
      )}
    </div>
  );
}