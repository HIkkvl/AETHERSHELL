import { useEffect, useState } from 'react';
import {
  changeAccountPassword,
  changeStaffPassword,
  getAccountProfile,
  getClubSlug,
  getMyStaffShift,
  getStaffProfile,
  isAccountSession,
  listStaffShifts,
  type PanelProfile,
  type StaffShift,
} from './api';
import './Profile.css';

const ROLE_LABELS: Record<string, string> = {
  Super: 'Управляющий',
  Senior: 'Старший администратор',
  Admin: 'Администратор',
  Owner: 'Владелец',
  PlatformAdmin: 'Админ платформы',
};

const END_REASON_LABELS: Record<string, string> = {
  Manual: 'Вручную',
  Logout: 'Выход',
  Reauth: 'Повторный вход',
};

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

export default function Profile() {
  const accountSession = isAccountSession();
  const [profile, setProfile] = useState<PanelProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [pwdMsg, setPwdMsg] = useState<string | null>(null);
  const [pwdError, setPwdError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [currentShift, setCurrentShift] = useState<StaffShift | null>(null);
  const [recentShifts, setRecentShifts] = useState<StaffShift[]>([]);
  const [clubShifts, setClubShifts] = useState<StaffShift[]>([]);
  const [nowTick, setNowTick] = useState(() => Date.now());

  const canSeeClubShifts = !accountSession && (profile?.role === 'Senior' || profile?.role === 'Super');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    const load = accountSession ? getAccountProfile() : getStaffProfile();
    load
      .then((data) => {
        if (!cancelled) setProfile(data);
      })
      .catch(() => {
        if (!cancelled) setError('Не удалось загрузить профиль');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => { cancelled = true; };
  }, [accountSession]);

  useEffect(() => {
    if (accountSession) return;
    let cancelled = false;

    const loadShifts = async () => {
      try {
        const mine = await getMyStaffShift();
        if (cancelled) return;
        setCurrentShift(mine.current);
        setRecentShifts(mine.recent || []);
      } catch {
        /* история смен опциональна */
      }
    };

    loadShifts();
    return () => { cancelled = true; };
  }, [accountSession]);

  useEffect(() => {
    if (!canSeeClubShifts) return;
    let cancelled = false;
    listStaffShifts(40)
      .then((rows) => {
        if (!cancelled) setClubShifts(rows);
      })
      .catch(() => { /* история клуба опциональна */ });
    return () => { cancelled = true; };
  }, [canSeeClubShifts]);

  useEffect(() => {
    if (!currentShift?.isOpen) return;
    const id = window.setInterval(() => setNowTick(Date.now()), 30_000);
    return () => window.clearInterval(id);
  }, [currentShift?.isOpen]);

  const openDurationMinutes = currentShift?.isOpen
    ? Math.max(0, Math.floor((nowTick - new Date(currentShift.startedAt).getTime()) / 60_000))
    : currentShift?.durationMinutes ?? 0;

  const submitPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    setPwdMsg(null);
    setPwdError(null);

    if (newPassword.length < (accountSession ? 8 : 6)) {
      setPwdError(accountSession
        ? 'Новый пароль должен быть не короче 8 символов'
        : 'Новый пароль должен быть не короче 6 символов');
      return;
    }
    if (newPassword !== confirmPassword) {
      setPwdError('Пароли не совпадают');
      return;
    }

    setSaving(true);
    try {
      if (accountSession) {
        await changeAccountPassword(currentPassword, newPassword);
      } else {
        await changeStaffPassword(currentPassword, newPassword);
      }
      setPwdMsg('Пароль изменён');
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
    } catch (err: any) {
      setPwdError(err?.response?.data?.error || err?.response?.data || 'Не удалось сменить пароль');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return <div className="page profile-page"><div className="profile-card">Загрузка…</div></div>;
  }

  if (error || !profile) {
    return <div className="page profile-page"><div className="profile-card profile-error">{error || 'Профиль недоступен'}</div></div>;
  }

  const displayName = profile.displayName || profile.username || 'Пользователь';
  const roleLabel = ROLE_LABELS[profile.role] || profile.role;
  const clubSlug = getClubSlug();

  return (
    <div className="page profile-page">
      <div className="profile-grid">
        <section className="profile-card">
          <div className="profile-head">
            <div className="profile-avatar-lg">{displayName.slice(0, 1).toUpperCase()}</div>
            <div>
              <h2>{displayName}</h2>
              <p>{roleLabel}</p>
            </div>
          </div>

          <div className="profile-fields">
            {profile.username && (
              <div className="profile-field">
                <span>Логин</span>
                <b>{profile.username}</b>
              </div>
            )}
            {profile.email && (
              <div className="profile-field">
                <span>Email</span>
                <b>{profile.email}</b>
              </div>
            )}
            <div className="profile-field">
              <span>Роль в панели</span>
              <b>{roleLabel}</b>
            </div>
            {clubSlug && (
              <div className="profile-field">
                <span>Клуб</span>
                <b>/panel/{clubSlug}</b>
              </div>
            )}
            {typeof profile.clubsCount === 'number' && (
              <div className="profile-field">
                <span>Клубов в аккаунте</span>
                <b>{profile.clubsCount}</b>
              </div>
            )}
          </div>
        </section>

        {!accountSession && (
          <section className="profile-card">
            <h3>Рабочая смена</h3>
            <p className="profile-shift-hint">
              Смена начинается автоматически при входе в панель. При нажатии «Выйти»
              откроется отчёт по смене — после подтверждения смена завершится.
            </p>

            <div className={`profile-shift-status ${currentShift?.isOpen ? 'open' : 'closed'}`}>
              {currentShift?.isOpen ? (
                <>
                  <span className="profile-shift-badge">Смена идёт</span>
                  <div className="profile-field">
                    <span>Начало</span>
                    <b>{formatLocal(currentShift.startedAt)}</b>
                  </div>
                  <div className="profile-field">
                    <span>Длительность</span>
                    <b>{formatDuration(openDurationMinutes)}</b>
                  </div>
                </>
              ) : (
                <>
                  <span className="profile-shift-badge idle">Смена не начата</span>
                  <p className="profile-shift-idle">После входа в панель смена откроется сама.</p>
                </>
              )}
            </div>

            {recentShifts.length > 0 && (
              <div className="profile-shift-history">
                <h4>Мои смены</h4>
                <ul>
                  {recentShifts.slice(0, 8).map((s) => (
                    <li key={s.id}>
                      <span>{formatLocal(s.startedAt)}</span>
                      <span>
                        {s.isOpen
                          ? 'идёт'
                          : `${formatDuration(s.durationMinutes)}${s.endReason ? ` · ${END_REASON_LABELS[s.endReason] || s.endReason}` : ''}`}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </section>
        )}

        <section className="profile-card">
          <h3>Сменить пароль</h3>
          <form className="profile-pwd-form" onSubmit={submitPassword}>
            <label>
              Текущий пароль
              <input
                type="password"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                required
              />
            </label>
            <label>
              Новый пароль
              <input
                type="password"
                autoComplete="new-password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                required
              />
            </label>
            <label>
              Повторите пароль
              <input
                type="password"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                required
              />
            </label>
            {pwdError && <p className="profile-pwd-error">{pwdError}</p>}
            {pwdMsg && <p className="profile-pwd-ok">{pwdMsg}</p>}
            <button type="submit" className="profile-save-btn" disabled={saving}>
              {saving ? 'Сохранение…' : 'Сохранить пароль'}
            </button>
          </form>
        </section>

        {canSeeClubShifts && clubShifts.length > 0 && (
          <section className="profile-card profile-card-wide">
            <h3>Смены персонала</h3>
            <div className="profile-shift-table-wrap">
              <table className="profile-shift-table">
                <thead>
                  <tr>
                    <th>Сотрудник</th>
                    <th>Начало</th>
                    <th>Конец</th>
                    <th>Длительность</th>
                    <th>Причина</th>
                  </tr>
                </thead>
                <tbody>
                  {clubShifts.map((s) => (
                    <tr key={s.id}>
                      <td>{s.username}</td>
                      <td>{formatLocal(s.startedAt)}</td>
                      <td>{s.endedAt ? formatLocal(s.endedAt) : '—'}</td>
                      <td>{s.isOpen ? 'идёт' : formatDuration(s.durationMinutes)}</td>
                      <td>{s.endReason ? (END_REASON_LABELS[s.endReason] || s.endReason) : (s.isOpen ? '—' : '—')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )}
      </div>
    </div>
  );
}
