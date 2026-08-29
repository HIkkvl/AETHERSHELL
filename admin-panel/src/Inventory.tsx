import { useEffect, useMemo, useState } from 'react';
import {
  adjustStock,
  getInventory,
  getProductMovements,
  getStockMovements,
  type Product,
  type StockMovement,
} from './api';
import { useClubLive } from './useClubLive';
import './Inventory.css';

type ModalMode = 'in' | 'out' | 'set' | null;

const KIND_LABEL: Record<string, string> = {
  In: 'Приход',
  Out: 'Уход',
  Order: 'Заказ',
  OrderCancel: 'Отмена заказа',
  Adjustment: 'Корректировка',
};

function formatWhen(iso: string) {
  try {
    return new Date(iso).toLocaleString('ru-RU');
  } catch {
    return iso;
  }
}

export default function Inventory() {
  const [products, setProducts] = useState<Product[]>([]);
  const [movements, setMovements] = useState<StockMovement[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Product | null>(null);
  const [productHistory, setProductHistory] = useState<StockMovement[]>([]);
  const [modal, setModal] = useState<ModalMode>(null);
  const [qty, setQty] = useState(1);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    try {
      const [inv, hist] = await Promise.all([getInventory(), getStockMovements(80)]);
      setProducts(Array.isArray(inv) ? inv : []);
      setMovements(Array.isArray(hist) ? hist : []);
    } catch (e) {
      console.error(e);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  useClubLive('products', () => {
    void load();
  });

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return products;
    return products.filter(
      (p) =>
        p.name.toLowerCase().includes(q) ||
        (p.category || '').toLowerCase().includes(q)
    );
  }, [products, search]);

  const openModal = (product: Product, mode: ModalMode) => {
    setSelected(product);
    setModal(mode);
    setQty(mode === 'set' ? product.stockQty ?? 0 : 1);
    setReason('');
    setError(null);
  };

  const openHistory = async (product: Product) => {
    setSelected(product);
    setModal(null);
    try {
      const rows = await getProductMovements(product.id, 60);
      setProductHistory(Array.isArray(rows) ? rows : []);
    } catch {
      setProductHistory([]);
    }
  };

  const submit = async () => {
    if (!selected || !modal) return;
    if (qty < 0 || (modal !== 'set' && qty === 0)) {
      setError('Укажите количество');
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const body =
        modal === 'in'
          ? { delta: qty, reason: reason || 'Приход' }
          : modal === 'out'
            ? { delta: -qty, reason: reason || 'Уход' }
            : { setTo: qty, reason: reason || 'Корректировка' };

      await adjustStock(selected.id, body);
      setModal(null);
      setSelected(null);
      await load();
    } catch (e: any) {
      const msg =
        e?.response?.data?.error ||
        e?.response?.data ||
        e?.message ||
        'Не удалось изменить остаток';
      setError(typeof msg === 'string' ? msg : 'Ошибка');
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <div className="inventory-page">Загрузка склада…</div>;

  return (
    <div className="inventory-page">
      <div className="inventory-header">
        <div>
          <h2 className="page-title">Учёт товаров</h2>
          <p className="inventory-sub">Приход, уход и история остатков меню бара</p>
        </div>
        <input
          className="inventory-search"
          placeholder="Поиск по названию или категории"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </div>

      <div className="inventory-layout">
        <section className="inventory-card">
          <h3>Остатки</h3>
          <div className="inventory-table-wrap">
            <table className="inventory-table">
              <thead>
                <tr>
                  <th>Товар</th>
                  <th>Категория</th>
                  <th>Остаток</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((p) => {
                  const stock = p.stockQty ?? 0;
                  return (
                    <tr key={p.id} className={stock <= 0 ? 'is-empty' : stock <= 5 ? 'is-low' : ''}>
                      <td>
                        <button type="button" className="linkish" onClick={() => void openHistory(p)}>
                          {p.name}
                        </button>
                        {!p.isAvailable && <span className="badge-hidden">скрыт</span>}
                      </td>
                      <td>{p.category || '—'}</td>
                      <td className="stock-cell">{stock}</td>
                      <td className="actions-cell">
                        <button type="button" className="btn-in" onClick={() => openModal(p, 'in')}>
                          Приход
                        </button>
                        <button type="button" className="btn-out" onClick={() => openModal(p, 'out')}>
                          Уход
                        </button>
                        <button type="button" className="btn-set" onClick={() => openModal(p, 'set')}>
                          = 
                        </button>
                      </td>
                    </tr>
                  );
                })}
                {filtered.length === 0 && (
                  <tr>
                    <td colSpan={4}>Товаров нет — добавьте их в «Меню»</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="inventory-card">
          <h3>
            {selected && !modal ? `История: ${selected.name}` : 'Последние движения'}
          </h3>
          <div className="inventory-table-wrap">
            <table className="inventory-table compact">
              <thead>
                <tr>
                  <th>Когда</th>
                  {!selected || modal ? <th>Товар</th> : null}
                  <th>Тип</th>
                  <th>±</th>
                  <th>Ост.</th>
                  <th>Кто / причина</th>
                </tr>
              </thead>
              <tbody>
                {(selected && !modal ? productHistory : movements).map((m) => (
                  <tr key={m.id}>
                    <td>{formatWhen(m.createdAt)}</td>
                    {(!selected || modal) && <td>{m.productName || '—'}</td>}
                    <td>{KIND_LABEL[m.kind] || m.kind}</td>
                    <td className={m.delta >= 0 ? 'plus' : 'minus'}>
                      {m.delta > 0 ? `+${m.delta}` : m.delta}
                    </td>
                    <td>{m.balanceAfter}</td>
                    <td>
                      <div>{m.createdBy}</div>
                      <div className="muted">{m.reason}{m.orderId ? ` · #${m.orderId}` : ''}</div>
                    </td>
                  </tr>
                ))}
                {(selected && !modal ? productHistory : movements).length === 0 && (
                  <tr>
                    <td colSpan={6}>Пока нет движений</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
          {selected && !modal && (
            <button type="button" className="btn-clear" onClick={() => setSelected(null)}>
              Показать все движения
            </button>
          )}
        </section>
      </div>

      {modal && selected && (
        <div className="inventory-modal-backdrop" onClick={() => !busy && setModal(null)}>
          <div className="inventory-modal" onClick={(e) => e.stopPropagation()}>
            <h3>
              {modal === 'in' && 'Приход'}
              {modal === 'out' && 'Уход'}
              {modal === 'set' && 'Установить остаток'}
              {': '}
              {selected.name}
            </h3>
            <p className="muted">Сейчас на складе: {selected.stockQty ?? 0}</p>
            <label>
              {modal === 'set' ? 'Новый остаток' : 'Количество'}
              <input
                type="number"
                min={0}
                value={qty}
                onChange={(e) => setQty(Number(e.target.value))}
              />
            </label>
            <label>
              Причина
              <input
                type="text"
                value={reason}
                placeholder={modal === 'in' ? 'Закупка / поставка' : modal === 'out' ? 'Списание / порча' : 'Инвентаризация'}
                onChange={(e) => setReason(e.target.value)}
              />
            </label>
            {error && <div className="inventory-error">{error}</div>}
            <div className="inventory-modal-actions">
              <button type="button" disabled={busy} onClick={() => setModal(null)}>
                Отмена
              </button>
              <button type="button" className="primary" disabled={busy} onClick={() => void submit()}>
                {busy ? '…' : 'Сохранить'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
