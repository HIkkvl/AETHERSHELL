import { useEffect, useMemo, useState } from "react";
import {
    getClubSettings,
    updateClubSettings,
    getLoyaltyClients,
    type ClubSettings,
    type LoyaltyClient,
} from "./api";
import { useClubLive } from "./useClubLive";
import "./Loyalty.css";

/// Пороги уровней по той же формуле, что и на сервере: каждый следующий процент
/// стоит дороже предыдущего на величину шага.
function buildLevels(firstThreshold: number, step: number, maxPercent: number) {
    const levels: { percent: number; totalSpent: number }[] = [];
    let threshold = firstThreshold;
    let total = 0;

    if (firstThreshold <= 0) return levels;

    for (let percent = 1; percent <= maxPercent; percent++) {
        total += threshold;
        levels.push({ percent, totalSpent: total });
        threshold += step;
    }

    return levels;
}

function formatMoney(value: number) {
    return `${Math.round(value).toLocaleString('ru-RU')} ₸`;
}

export default function Loyalty() {
    const [settings, setSettings] = useState<ClubSettings | null>(null);
    const [clients, setClients] = useState<LoyaltyClient[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [message, setMessage] = useState<string | null>(null);

    const [firstThreshold, setFirstThreshold] = useState(50000);
    const [step, setStep] = useState(5000);
    const [maxPercent, setMaxPercent] = useState(20);
    const [enableShop, setEnableShop] = useState(true);

    const load = async () => {
        setLoading(true);
        try {
            const [s, c] = await Promise.all([getClubSettings(), getLoyaltyClients()]);
            setSettings(s);
            setFirstThreshold(s.loyaltyFirstThreshold);
            setStep(s.loyaltyStep);
            setMaxPercent(s.maxDiscountPercent);
            setEnableShop(s.enableShop !== false);
            setClients(c);
        } catch {
            setMessage('Не удалось загрузить настройки лояльности');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { load(); }, []);

    useClubLive(['loyalty', 'clients'], () => { void load(); });

    const levels = useMemo(
        () => buildLevels(Number(firstThreshold), Number(step), Number(maxPercent)),
        [firstThreshold, step, maxPercent]
    );

    const dirty = !!settings && (
        settings.loyaltyFirstThreshold !== Number(firstThreshold) ||
        settings.loyaltyStep !== Number(step) ||
        settings.maxDiscountPercent !== Number(maxPercent) ||
        (settings.enableShop !== false) !== enableShop
    );

    const handleSave = async () => {
        if (!settings) return;

        if (Number(firstThreshold) <= 0) {
            setMessage('Первый порог должен быть больше нуля');
            return;
        }

        setSaving(true);
        setMessage(null);
        try {
            await updateClubSettings({
                loyaltyFirstThreshold: Number(firstThreshold),
                loyaltyStep: Number(step),
                maxDiscountPercent: Number(maxPercent),
                requireComputerApproval: settings.requireComputerApproval,
                enableShop,
            });
            setMessage('Настройки сохранены');
            await load();
        } catch {
            setMessage('Не удалось сохранить настройки');
        } finally {
            setSaving(false);
        }
    };

    if (loading) {
        return <div className="page loyalty-container"><p className="loyalty-muted">Загрузка...</p></div>;
    }

    return (
        <div className="page loyalty-container">
            <div className="page-toolbar">
                <p className="page-subtitle">Программа лояльности</p>
            </div>
            <div className="loyalty-grid">
                <section className="loyalty-card">
                    <h2>Настройки скидок</h2>
                    <p className="loyalty-muted">
                        Скидка растёт по накопленным тратам клиента. Каждый следующий процент
                        стоит дороже предыдущего на величину шага.
                    </p>

                    <div className="loyalty-field">
                        <label>Первый порог, ₸</label>
                        <input
                            type="number"
                            min={1}
                            value={firstThreshold}
                            onChange={(e) => setFirstThreshold(Number(e.target.value))}
                        />
                    </div>

                    <div className="loyalty-field">
                        <label>Шаг между уровнями, ₸</label>
                        <input
                            type="number"
                            min={0}
                            value={step}
                            onChange={(e) => setStep(Number(e.target.value))}
                        />
                    </div>

                    <div className="loyalty-field">
                        <label>Максимальная скидка, %</label>
                        <input
                            type="number"
                            min={0}
                            max={90}
                            value={maxPercent}
                            onChange={(e) => setMaxPercent(Number(e.target.value))}
                        />
                    </div>

                    <div className="loyalty-field">
                        <label>Магазин / еда в шелле</label>
                        <label style={{ display: 'flex', alignItems: 'center', gap: 10, textTransform: 'none', letterSpacing: 0, fontWeight: 600, color: 'var(--ink)', cursor: 'pointer' }}>
                            <input
                                type="checkbox"
                                checked={enableShop}
                                onChange={(e) => setEnableShop(e.target.checked)}
                            />
                            Показывать вкладку «Еда» клиентам
                        </label>
                        <p className="loyalty-muted" style={{ marginTop: 6 }}>
                            Выключите, если в этом зале нет бара/магазина.
                        </p>
                    </div>

                    <button className="loyalty-save" onClick={handleSave} disabled={saving || !dirty}>
                        {saving ? 'Сохранение...' : dirty ? 'Сохранить' : 'Сохранено'}
                    </button>

                    {message && <p className="loyalty-message">{message}</p>}
                </section>

                <section className="loyalty-card">
                    <h2>Уровни</h2>
                    <p className="loyalty-muted">Предпросмотр по текущим значениям в форме.</p>

                    <div className="loyalty-levels">
                        <table className="loyalty-table">
                            <thead>
                                <tr><th>Скидка</th><th>Накоплено трат</th></tr>
                            </thead>
                            <tbody>
                                {levels.map(l => (
                                    <tr key={l.percent}>
                                        <td><b>{l.percent}%</b></td>
                                        <td>{formatMoney(l.totalSpent)}</td>
                                    </tr>
                                ))}
                                {levels.length === 0 && (
                                    <tr><td colSpan={2} className="loyalty-muted">Скидки отключены</td></tr>
                                )}
                            </tbody>
                        </table>
                    </div>
                </section>
            </div>

            <section className="loyalty-card">
                <h2>Клиенты по тратам</h2>
                {clients.length === 0 ? (
                    <p className="loyalty-muted">Пока нет клиентов с покупками</p>
                ) : (
                    <table className="loyalty-table wide">
                        <thead>
                            <tr>
                                <th>#</th>
                                <th>Клиент</th>
                                <th>Потрачено</th>
                                <th>Скидка</th>
                                <th>До следующей</th>
                                <th>Баланс</th>
                            </tr>
                        </thead>
                        <tbody>
                            {clients.map((c, idx) => (
                                <tr key={c.id}>
                                    <td className="loyalty-muted">{idx + 1}</td>
                                    <td><b>{c.username}</b></td>
                                    <td>{formatMoney(c.totalSpent)}</td>
                                    <td><span className="loyalty-badge">{c.discountPercent}%</span></td>
                                    <td>{c.nextThreshold === null ? 'максимум' : formatMoney(c.nextThreshold)}</td>
                                    <td>{formatMoney(c.balance)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </section>
        </div>
    );
}
