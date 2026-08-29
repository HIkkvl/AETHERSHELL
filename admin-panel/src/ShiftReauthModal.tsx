import type { StaffShift, StaffShiftSummary } from './api';
import './ShiftReauthModal.css';

function formatLocal(iso: string) {
  try {
    return new Date(iso).toLocaleString('ru-RU', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

function formatDuration(minutes: number) {
  if (minutes < 60) return `${minutes} мин`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m > 0 ? `${h} ч ${m} мин` : `${h} ч`;
}

interface Props {
  shift: StaffShift;
  summary: StaffShiftSummary;
  busy?: boolean;
  onConfirm: () => void;
  onCancel?: () => void;
}

export default function ShiftReauthModal({ shift, summary, busy, onConfirm, onCancel }: Props) {
  return (
    <div className="shift-reauth-backdrop" role="dialog" aria-modal="true" aria-labelledby="shift-reauth-title">
      <div className="shift-reauth-modal">
        <h2 id="shift-reauth-title">Окончание смены</h2>
        <p className="shift-reauth-lead">
          Перед выходом подтвердите завершение смены. Ниже — краткий отчёт по вашим действиям.
        </p>

        <div className="shift-reauth-stats">
          <div>
            <span>Начало</span>
            <b>{formatLocal(shift.startedAt)}</b>
          </div>
          <div>
            <span>Длительность</span>
            <b>{formatDuration(summary.durationMinutes)}</b>
          </div>
          <div>
            <span>Действий</span>
            <b>{summary.totalActions}</b>
          </div>
        </div>

        {summary.byType.length > 0 && (
          <div className="shift-reauth-block">
            <h3>Что было за смену</h3>
            <ul className="shift-reauth-types">
              {summary.byType.map((row) => (
                <li key={row.type}>
                  <span>{row.label}</span>
                  <b>{row.count}</b>
                </li>
              ))}
            </ul>
          </div>
        )}

        {summary.recent.length > 0 && (
          <div className="shift-reauth-block">
            <h3>Последние действия</h3>
            <ul className="shift-reauth-recent">
              {summary.recent.map((row) => (
                <li key={row.id}>
                  <div className="shift-reauth-recent-top">
                    <b>{row.label}</b>
                    <span>{formatLocal(row.createdAt)}</span>
                  </div>
                  <div className="shift-reauth-recent-details">
                    {row.target ? `${row.target}: ` : ''}{row.details}
                  </div>
                </li>
              ))}
            </ul>
          </div>
        )}

        {summary.totalActions === 0 && (
          <p className="shift-reauth-empty">За эту смену зафиксированных действий пока нет.</p>
        )}

        <div className="shift-reauth-actions">
          {onCancel && (
            <button
              type="button"
              className="shift-reauth-cancel"
              disabled={busy}
              onClick={onCancel}
            >
              Остаться
            </button>
          )}
          <button
            type="button"
            className="shift-reauth-confirm"
            disabled={busy}
            onClick={onConfirm}
          >
            {busy ? 'Сохранение…' : 'Завершить смену и выйти'}
          </button>
        </div>
      </div>
    </div>
  );
}
