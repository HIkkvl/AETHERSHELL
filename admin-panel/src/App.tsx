import React, { useState, useEffect, useRef } from 'react'; // <--- ДОБАВЛЕНО React
import Login from './Login';
import Dashboard from './Dashboard';
import ClubMap from './ClubMap';
import Computers from './Computers';
import Apps from './Apps';
import Tariffs from './Tariffs';
import Users from './Users';
import History from './History';
import Products from './Products';
import Inventory from './Inventory';
import Orders from './Orders';
import Chat from './Chat';
import Banners from './Banners';
import Loyalty from './Loyalty';
import Profile from './Profile';
import ShiftReauthModal from './ShiftReauthModal';
import {
  getOrders,
  isAccountSession,
  endStaffShift,
  getClubSlug,
  panelUrlForClub,
  getMyStaffShift,
  enterStaffShift,
  getStaffShiftSummary,
  setActiveShiftId,
  type StaffShift,
  type StaffShiftSummary,
} from './api';
import './App.css';
import * as signalR from "@microsoft/signalr";

import { getHubUrl } from './api';

// Хелпер для оповещения всех компонентов об изменении
export const notifyStorageChange = () => {
    window.dispatchEvent(new Event("club_storage_update"));
};

// Хелпер для оповещения списка заказов
export const notifyOrdersUpdate = () => {
    window.dispatchEvent(new Event("club_orders_update"));
};

/** Realtime-пинги с сервера (SignalR → страницы админки). */
export type ClubLiveKind =
  | 'computers'
  | 'clients'
  | 'apps'
  | 'products'
  | 'tariffs'
  | 'banners'
  | 'loyalty'
  | 'dashboard'
  | 'orders';

export const notifyClubLive = (kind: ClubLiveKind) => {
  window.dispatchEvent(new CustomEvent('club_live_update', { detail: kind }));
};

const PAGE_TITLES: Record<string, string> = {
  dashboard: 'Обзор',
  map: 'Карта клуба',
  computers: 'Компьютеры',
  clients: 'Клиенты',
  orders: 'Заказы',
  chat: 'Чат',
  apps: 'Игры и ПО',
  tariffs: 'Тарифы',
  products: 'Меню',
  inventory: 'Учёт товаров',
  users: 'Сотрудники',
  banners: 'Баннеры',
  loyalty: 'Лояльность',
  history: 'Логи',
  profile: 'Профиль',
};

const ROLE_LABELS: Record<string, string> = {
  Super: 'Управляющий',
  Senior: 'Старший админ',
  Admin: 'Администратор',
  Owner: 'Владелец',
  PlatformAdmin: 'Админ платформы',
};

function readStoredUserName(): string {
  const saved = localStorage.getItem('userName')?.trim();
  if (saved) return saved;
  try {
    const token = localStorage.getItem('authToken');
    if (!token) return '';
    const payload = JSON.parse(atob(token.split('.')[1]));
    return (
      payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name']
      || payload['unique_name']
      || payload['name']
      || ''
    );
  } catch {
    return '';
  }
}

function App() {
  const [token, setToken] = useState<string | null>(localStorage.getItem('authToken'));
  const [role, setRole] = useState<string>(localStorage.getItem('userRole') || 'Admin');
  const [userName, setUserName] = useState<string>(() => readStoredUserName());

  // Кабинет есть только у владельца клуба и админа платформы. Персоналу зала
  // возвращаться некуда, поэтому кнопку он не видит.
  const [hasCabinet, setHasCabinet] = useState<boolean>(isAccountSession());

  type PageType = 'dashboard' | 'map' | 'computers' | 'clients' | 'apps' | 'tariffs' | 'users' | 'history' | 'products' | 'inventory' | 'orders' | 'chat' | 'banners' | 'loyalty' | 'profile';
  
  const [currentPage, setCurrentPage] = useState<PageType>('map');
  const currentPageRef = useRef<PageType>(currentPage);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);
  const [shiftOpen, setShiftOpen] = useState(false);
  const [reauthShift, setReauthShift] = useState<StaffShift | null>(null);
  const [reauthSummary, setReauthSummary] = useState<StaffShiftSummary | null>(null);
  const [reauthBusy, setReauthBusy] = useState(false);
  const staffSession = !isAccountSession();
  const shiftBootstrapped = useRef(false);

  const [totalUnread, setTotalUnread] = useState<number>(0); // Для чата
  const [newOrdersCount, setNewOrdersCount] = useState<number>(0); // Для заказов

  const canAccess = (requiredRoles: string[]) => {
      if (role === 'Super') return true;
      return requiredRoles.includes(role);
  };

  // Чтение непрочитанных сообщений из памяти
  const calcUnreadFromStorage = () => {
    try {
        const saved = localStorage.getItem("chat_unread_store");
        const store = saved ? JSON.parse(saved) : {};
        return Object.values(store).reduce((acc: number, curr: any) => acc + (Number(curr) || 0), 0) as number;
    } catch { return 0; }
  };

  // Первичная загрузка и слушатели
  useEffect(() => {
    const savedToken = localStorage.getItem('authToken');
    const savedRole = localStorage.getItem('userRole');

    // ПРОВЕРКА: Если роль недопустимая — сразу выходим
    const allowedRoles = ['Admin', 'Senior', 'Super'];
    if (savedToken && savedRole && !allowedRoles.includes(savedRole)) {
        localStorage.removeItem('authToken');
        localStorage.removeItem('userRole');
        setToken(null);
        return;
    }

    if (savedToken) setToken(savedToken);
    if (savedRole) setRole(savedRole);
    setHasCabinet(isAccountSession());

    const name = readStoredUserName();
    if (name) {
      setUserName(name);
      if (!localStorage.getItem('userName')) localStorage.setItem('userName', name);
    }
    
    // ... (дальше код подсчета чата и заказов оставляем как есть)
    
    // 1. Считаем чат
    setTotalUnread(calcUnreadFromStorage());

    // 2. Считаем активные заказы (запрос к API)
    if (savedToken) {
        getOrders('new').then(orders => {
            if (Array.isArray(orders)) {
                setNewOrdersCount(orders.length);
            }
        }).catch(console.error);
    }

    // Смена персонала: статус для меню в шапке
    if (savedToken && !isAccountSession()) {
      getMyStaffShift()
        .then((mine) => setShiftOpen(!!mine.current?.isOpen))
        .catch(() => { /* меню покажет статус позже */ });
    }

    // Слушаем изменения чата
    const handleCustomUpdate = () => setTotalUnread(calcUnreadFromStorage());
    window.addEventListener("club_storage_update", handleCustomUpdate);
    window.addEventListener("storage", handleCustomUpdate);

    return () => {
        window.removeEventListener("club_storage_update", handleCustomUpdate);
        window.removeEventListener("storage", handleCustomUpdate);
    };
  }, []);

  useEffect(() => { currentPageRef.current = currentPage; }, [currentPage]);

  // Автостарт смены / окно подтверждения при повторном входе.
  useEffect(() => {
    if (!token || isAccountSession()) {
      shiftBootstrapped.current = false;
      return;
    }
    if (shiftBootstrapped.current) return;
    shiftBootstrapped.current = true;

    let cancelled = false;
    (async () => {
      try {
        const result = await enterStaffShift();
        if (cancelled) return;

        if (result.shift?.id != null) {
          setActiveShiftId(result.shift.id);
          setShiftOpen(!!result.shift.isOpen || result.status === 'started' || result.status === 'active');
        }
      } catch {
        shiftBootstrapped.current = false;
      }
    })();

    return () => { cancelled = true; };
  }, [token]);

  useEffect(() => {
    if (!userMenuOpen) return;
    const onDoc = (e: MouseEvent) => {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target as Node)) {
        setUserMenuOpen(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setUserMenuOpen(false);
    };
    document.addEventListener('mousedown', onDoc);
    window.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDoc);
      window.removeEventListener('keydown', onKey);
    };
  }, [userMenuOpen]);

  // SignalR
  useEffect(() => {
    if (!token) return; 
    
    const conn = new signalR.HubConnectionBuilder()
        .withUrl(getHubUrl(), {
            accessTokenFactory: () => localStorage.getItem('authToken') || '',
            skipNegotiation: true,
            transport: signalR.HttpTransportType.WebSockets
        })
        .withAutomaticReconnect()
        .build();

    conn.start()
        .then(() => conn.invoke("JoinAdminGroup"))
        .catch(console.error);
    
    // --- ЧАТ ---
    conn.on("ReceiveMessageFromClient", (pcName: string) => {
        if (!pcName) return;
        if (currentPageRef.current === 'chat') return;

        try {
             const saved = localStorage.getItem("chat_unread_store");
             const store = saved ? JSON.parse(saved) : {};
             const key = pcName.toLowerCase();
             store[key] = (store[key] || 0) + 1;
             localStorage.setItem("chat_unread_store", JSON.stringify(store));
             
             notifyStorageChange();

             const audio = new Audio("/notification.mp3");
             audio.play().catch(e => console.warn("Sound blocked:", e));
        } catch (e) { console.error(e); }
    });
    
    // --- ЗАКАЗЫ (НОВОЕ) ---
    conn.on("ReceiveOrderUpdate", (orderId: number, status: string) => {
        // Оповещаем страницу заказов
        notifyOrdersUpdate();
        notifyClubLive('orders');

        // Если пришел НОВЫЙ заказ
        if (status === 'New') {
            // Играем звук
            const audio = new Audio("/notification.mp3");
            audio.play().catch(console.warn);

            // Если мы НЕ на странице заказов - увеличиваем счетчик
            if (currentPageRef.current !== 'orders') {
                setNewOrdersCount(prev => prev + 1);
            }
        }
    });
    
    conn.on("ChatCleared", () => {
        setTimeout(() => notifyStorageChange(), 500);
    });

    // --- LIVE SYNC: ПК / каталоги / клиенты (шелл ↔ панель) ---
    conn.on("DashboardUpdate", () => {
      notifyClubLive('computers');
      notifyClubLive('dashboard');
    });
    conn.on("NewPcPendingApproval", () => notifyClubLive('computers'));
    conn.on("ComputersUpdated", () => {
      notifyClubLive('computers');
      notifyClubLive('dashboard');
    });
    conn.on("ClientsUpdated", () => notifyClubLive('clients'));
    conn.on("AppsUpdated", () => notifyClubLive('apps'));
    conn.on("ProductsUpdated", () => notifyClubLive('products'));
    conn.on("TariffsUpdated", () => notifyClubLive('tariffs'));
    conn.on("BannersUpdated", () => notifyClubLive('banners'));
    conn.on("LoyaltyUpdated", () => notifyClubLive('loyalty'));
    
    return () => { conn.stop(); };
  }, [token]);

  const changePage = (page: PageType) => {
      setCurrentPage(page);
      setSidebarOpen(false); // на мобильном закрыть меню после выбора
      if (page === 'chat') setTimeout(() => notifyStorageChange(), 100);
      if (page === 'orders') setNewOrdersCount(0);
  };

  const toggleUserMenu = async () => {
    const next = !userMenuOpen;
    setUserMenuOpen(next);
    if (next && staffSession) {
      try {
        const mine = await getMyStaffShift();
        setShiftOpen(!!mine.current?.isOpen);
      } catch { /* leave previous state */ }
    }
  };

  const goToProfile = () => {
    setUserMenuOpen(false);
    changePage('profile');
  };

  const finishLogout = () => {
    setActiveShiftId(null);
    shiftBootstrapped.current = false;

    const clubSlug = getClubSlug();
    const panelLoginUrl = clubSlug
      ? panelUrlForClub(clubSlug)
      : (window.location.pathname.startsWith('/panel') ? window.location.pathname : '/panel');

    localStorage.removeItem('authToken');
    localStorage.removeItem('userRole');
    localStorage.removeItem('userName');
    localStorage.removeItem('chat_unread_store');
    localStorage.removeItem('cabinet_token');
    localStorage.removeItem('cabinet_user');
    setToken(null);
    setReauthShift(null);
    setReauthSummary(null);
    setShiftOpen(false);
    window.location.href = panelLoginUrl;
  };

  const confirmLogoutShift = async () => {
    setReauthBusy(true);
    try {
      await endStaffShift('Logout');
      setActiveShiftId(null);
      setReauthShift(null);
      setReauthSummary(null);
      finishLogout();
    } catch (e: any) {
      alert(e?.response?.data?.error || e?.response?.data || 'Не удалось завершить смену');
      setReauthBusy(false);
    }
  };

  // Возврат в кабинет владельца: сессия кабинета живёт под своим ключом,
  // поэтому её не трогаем — иначе на /kabinet пришлось бы логиниться заново.
  const handleBackToCabinet = () => {
    window.location.href = '/kabinet';
  };

  const handleLogout = async () => {
    setUserMenuOpen(false);

    if (staffSession) {
      try {
        const data = await getStaffShiftSummary();
        if (data.hasOpen && data.shift && data.summary) {
          setReauthShift(data.shift);
          setReauthSummary(data.summary);
          return;
        }
      } catch {
        /* нет отчёта — просто выходим */
      }
    }

    finishLogout();
  };

  if (!token) {
    return <Login onLoginSuccess={() => {
        setToken(localStorage.getItem('authToken'));
        setRole(localStorage.getItem('userRole') || 'Admin');
        setUserName(readStoredUserName());
        setHasCabinet(isAccountSession());
    }} />;
  }

  return (
    <div className={`app-container ${sidebarOpen ? 'sidebar-open' : ''}`}>
      {reauthShift && reauthSummary && (
        <ShiftReauthModal
          shift={reauthShift}
          summary={reauthSummary}
          busy={reauthBusy}
          onConfirm={() => { void confirmLogoutShift(); }}
          onCancel={() => {
            if (reauthBusy) return;
            setReauthShift(null);
            setReauthSummary(null);
          }}
        />
      )}
      <div className="sidebar-overlay" aria-hidden={!sidebarOpen} onClick={() => setSidebarOpen(false)} />
      <aside className="sidebar">
        <div className="sidebar-logo">
          <img src={`${import.meta.env.BASE_URL}images/logo.png`} alt="Aether" className="sidebar-logo-img" />
          <span className="sidebar-logo-text">Aether</span>
        </div>
        <div className="sidebar-sub">Панель управления</div>
        
        <nav className="nav-menu">
          <div className="nav-section">Основное</div>
          <button className={`nav-link ${currentPage === 'dashboard' ? 'active' : ''}`} onClick={() => changePage('dashboard')}>
            <svg viewBox="0 0 24 24"><rect x="3" y="3" width="7" height="7" rx="2"/><rect x="14" y="3" width="7" height="7" rx="2"/><rect x="3" y="14" width="7" height="7" rx="2"/><rect x="14" y="14" width="7" height="7" rx="2"/></svg>
            Обзор
          </button>
          <button className={`nav-link ${currentPage === 'map' ? 'active' : ''}`} onClick={() => changePage('map')}>
            <svg viewBox="0 0 24 24"><polygon points="1 6 1 22 8 18 16 22 23 18 23 2 16 6 8 2 1 6"/><line x1="8" y1="2" x2="8" y2="18"/><line x1="16" y1="6" x2="16" y2="22"/></svg>
            Карта клуба
          </button>
          <button className={`nav-link ${currentPage === 'computers' ? 'active' : ''}`} onClick={() => changePage('computers')}>
            <svg viewBox="0 0 24 24"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
            Компьютеры
          </button>
          <button className={`nav-link ${currentPage === 'clients' ? 'active' : ''}`} onClick={() => changePage('clients')}>
            <svg viewBox="0 0 24 24"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
            Клиенты
          </button>
          
          <button className={`nav-link ${currentPage === 'orders' ? 'active' : ''}`} onClick={() => changePage('orders')}>
            <svg viewBox="0 0 24 24"><path d="M6 2L3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4z"/><line x1="3" y1="6" x2="21" y2="6"/><path d="M16 10a4 4 0 0 1-8 0"/></svg>
            Заказы
            {newOrdersCount > 0 && <span className="menu-badge">{newOrdersCount}</span>}
          </button>

          <button className={`nav-link ${currentPage === 'inventory' ? 'active' : ''}`} onClick={() => changePage('inventory')}>
            <svg viewBox="0 0 24 24"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><polyline points="3.27 6.96 12 12.01 20.73 6.96"/><line x1="12" y1="22.08" x2="12" y2="12"/></svg>
            Учёт товаров
          </button>

          <button className={`nav-link ${currentPage === 'chat' ? 'active' : ''}`} onClick={() => changePage('chat')}>
            <svg viewBox="0 0 24 24"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>
            Чат
            {totalUnread > 0 && <span className="menu-badge">{totalUnread}</span>}
          </button>

          {canAccess(['Senior']) && (
             <>
                <div className="nav-section">Управление</div>
                <button className={`nav-link ${currentPage === 'apps' ? 'active' : ''}`} onClick={() => changePage('apps')}>
                  <svg viewBox="0 0 24 24"><polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/></svg>
                  Игры и ПО
                </button>
                <button className={`nav-link ${currentPage === 'tariffs' ? 'active' : ''}`} onClick={() => changePage('tariffs')}>
                  <svg viewBox="0 0 24 24"><line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
                  Тарифы
                </button>
                <button className={`nav-link ${currentPage === 'products' ? 'active' : ''}`} onClick={() => changePage('products')}>
                  <svg viewBox="0 0 24 24"><rect x="1" y="4" width="22" height="16" rx="2" ry="2"/><line x1="1" y1="10" x2="23" y2="10"/></svg>
                  Меню
                </button>
             </>
          )}

          {canAccess([]) && (
             <>
                <div className="nav-section">Система</div>
                <button className={`nav-link ${currentPage === 'users' ? 'active' : ''}`} onClick={() => changePage('users')}>
                  <svg viewBox="0 0 24 24"><path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="8.5" cy="7" r="4"/><line x1="20" y1="8" x2="20" y2="14"/><line x1="23" y1="11" x2="17" y2="11"/></svg>
                  Сотрудники
                </button>
                <button className={`nav-link ${currentPage === 'banners' ? 'active' : ''}`} onClick={() => changePage('banners')}>
                  <svg viewBox="0 0 24 24"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
                  Баннеры
                </button>
                <button className={`nav-link ${currentPage === 'loyalty' ? 'active' : ''}`} onClick={() => changePage('loyalty')}>
                  <svg viewBox="0 0 24 24"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
                  Лояльность
                </button>
                <button className={`nav-link ${currentPage === 'history' ? 'active' : ''}`} onClick={() => changePage('history')}>
                  <svg viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>
                  Логи
                </button>
             </>
          )}
        </nav>

        <div className="sidebar-footer">
          {hasCabinet && (
            <button className="btn-cabinet" onClick={handleBackToCabinet}>
              <svg viewBox="0 0 24 24"><polyline points="15 18 9 12 15 6"/><path d="M3 12h12a6 6 0 0 1 6 6v1"/></svg>
              В кабинет
            </button>
          )}
          <button className="btn-logout" onClick={handleLogout}>
            <svg viewBox="0 0 24 24"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>
            Выйти
          </button>
        </div>
      </aside>

      <header className="mobile-header">
        <button type="button" className="mobile-menu-btn" onClick={() => setSidebarOpen(true)} aria-label="Меню">
          <svg viewBox="0 0 24 24" aria-hidden="true"><line x1="4" y1="7" x2="20" y2="7"/><line x1="4" y1="12" x2="20" y2="12"/><line x1="4" y1="17" x2="20" y2="17"/></svg>
        </button>
        <span className="mobile-title">Aether</span>
      </header>

      <main className="main-content">
        <div className="top-bar">
          <div className="page-title">{PAGE_TITLES[currentPage] || 'Обзор'}</div>
          <div className="top-bar-right">
            <div className={`user-menu ${userMenuOpen ? 'open' : ''}`} ref={userMenuRef}>
              <button
                type="button"
                className={`user-pill ${currentPage === 'profile' || userMenuOpen ? 'active' : ''}`}
                title="Меню профиля"
                aria-expanded={userMenuOpen}
                aria-haspopup="menu"
                onClick={() => { void toggleUserMenu(); }}
              >
                <div className="user-avatar">
                  <svg viewBox="0 0 24 24"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
                </div>
                <div>
                  <div className="user-pill-name">{userName || 'Администратор'}</div>
                  <div className="user-pill-role">
                    {ROLE_LABELS[role] || role}
                    {staffSession && shiftOpen ? ' · смена' : ''}
                  </div>
                </div>
              </button>

              {userMenuOpen && (
                <div className="user-menu-pop" role="menu">
                  <button type="button" className="user-menu-item" role="menuitem" onClick={goToProfile}>
                    Профиль
                  </button>
                  {staffSession && (
                    <div className="user-menu-hint">
                      {shiftOpen ? 'Смена идёт · завершение при выходе' : 'Смена начнётся при входе'}
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>

        {currentPage === 'dashboard' && <Dashboard onNavigateToComputers={() => changePage('computers')} />}
        {currentPage === 'map' && <ClubMap />}
        {currentPage === 'computers' && <Computers />}
        {currentPage === 'orders' && <Orders />}
        {currentPage === 'chat' && <Chat />}
        {currentPage === 'clients' && <Users mode="clients" />}
        {currentPage === 'users' && canAccess([]) && <Users mode="staff" />}
        {currentPage === 'apps' && canAccess(['Senior']) && <Apps />}
        {currentPage === 'tariffs' && canAccess(['Senior']) && <Tariffs />}
        {currentPage === 'products' && canAccess(['Senior']) && <Products />}
        {currentPage === 'inventory' && <Inventory />}
        {currentPage === 'loyalty' && canAccess([]) && <Loyalty />}
        {currentPage === 'history' && canAccess([]) && <History />}
        {currentPage === 'banners' && canAccess(['Senior']) && <Banners />}
        {currentPage === 'profile' && <Profile />}
      </main>
    </div>
  );
}

export default App;
