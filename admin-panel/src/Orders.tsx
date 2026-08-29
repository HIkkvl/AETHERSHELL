import { useEffect, useState } from "react";
import { getOrders, updateOrderStatus, type Order } from "./api";
import "./Orders.css";

type TabType = 'new' | 'processing' | 'ready' | 'history';

export default function Orders() {
  const [orders, setOrders] = useState<Order[]>([]);
  const [activeTab, setActiveTab] = useState<TabType>('new');

  const fetchOrders = async () => {
    try {
      const data = await getOrders(activeTab);
      setOrders(data);
    } catch (e) {
      console.error("Ошибка загрузки заказов");
    }
  };

  useEffect(() => {
    // 1. Загружаем сразу при открытии
    fetchOrders();

    // 2. Слушаем событие РЕАЛЬНОГО ВРЕМЕНИ от App.tsx
    const handleRealtimeUpdate = () => {
        console.log("🔔 Orders.tsx: Получено уведомление о обновлении!");
        fetchOrders();
    };
    
    window.addEventListener("club_orders_update", handleRealtimeUpdate);

    // 3. Резервный интервал (можно сделать реже, например раз в 15 сек, т.к. есть сокеты)
    const interval = setInterval(fetchOrders, 15000);

    return () => {
        window.removeEventListener("club_orders_update", handleRealtimeUpdate);
        clearInterval(interval);
    };
  }, [activeTab]); // Перезапускаем подписки при смене таба

  const handleStatus = async (id: number, status: string) => {
    await updateOrderStatus(id, status);
    // После нашего действия тоже обновляем список (хотя сокет тоже прилетит)
    fetchOrders(); 
  };

  return (
    <div className="page orders-container">
      <div className="page-toolbar orders-header">
        <p className="page-subtitle">Кухня и бар · только операционка зала</p>
        <div className="tabs ui-tabs" style={{ marginBottom: 0 }}>
            <button type="button" className={activeTab === 'new' ? 'active' : ''} onClick={() => setActiveTab('new')}>Новые</button>
            <button type="button" className={activeTab === 'processing' ? 'active' : ''} onClick={() => setActiveTab('processing')}>В работе</button>
            <button type="button" className={activeTab === 'ready' ? 'active' : ''} onClick={() => setActiveTab('ready')}>К выдаче</button>
            <button type="button" className={activeTab === 'history' ? 'active' : ''} onClick={() => setActiveTab('history')}>История</button>
        </div>
      </div>

      <div className="orders-grid">
        {orders.length === 0 && (
            <div style={{gridColumn: '1/-1', textAlign:'center', padding: '50px', color: 'var(--text-secondary)'}}>
                <h3>Нет активных заказов</h3>
                <p>В этой категории пока пусто.</p>
            </div>
        )}
        
        {orders.map(order => (
            <div key={order.id} className={`order-card ${order.status.toLowerCase()}`}>
                <div className="card-top">
                    <span className="pc-badge">Компьютер: {order.pcName}</span>
                    <span className="time">{order.time}</span>
                </div>
                
                <ul className="order-items">
                    {order.items.map((item, idx) => (
                        <li key={idx}>
                            <span>
                                <span className="item-qty">{item.quantity}x</span> 
                                {item.name}
                            </span>
                        </li>
                    ))}
                </ul>

                <div className="total-price">
                    {order.totalPrice} ₸
                </div>

                {/* Кнопки управления */}
                {activeTab === 'new' && order.status === 'New' && (
                    <div className="card-actions">
                        <button className="btn-process" onClick={() => handleStatus(order.id, 'Processing')}>
                            👨‍🍳 Готовить
                        </button>
                        <button className="btn-cancel" onClick={() => handleStatus(order.id, 'Cancelled')}>
                            Отмена
                        </button>
                    </div>
                )}
                
                {activeTab === 'processing' && order.status === 'Processing' && (
                    <div className="card-actions">
                        <button className="btn-done" onClick={() => handleStatus(order.id, 'Ready')}>
                            ✅ Готово
                        </button>
                        <button className="btn-cancel" onClick={() => handleStatus(order.id, 'Cancelled')}>
                            Отмена
                        </button>
                    </div>
                )}
                
                {activeTab === 'ready' && order.status === 'Ready' && (
                    <div className="card-actions">
                        <button className="btn-done" onClick={() => handleStatus(order.id, 'Completed')}>
                            🙌 Выдано
                        </button>
                        <button className="btn-cancel" onClick={() => handleStatus(order.id, 'Cancelled')}>
                            Отмена
                        </button>
                    </div>
                )}
                
                {activeTab === 'history' && (
                    <div className="status-label">
                        {order.status === 'Completed' ? '✅ Выполнен' : 
                         order.status === 'Cancelled' ? '❌ Отменен' : order.status}
                    </div>
                )}
            </div>
        ))}
      </div>
    </div>
  );
}