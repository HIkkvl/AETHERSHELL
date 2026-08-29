import axios from 'axios';

// ========== АДРЕС СЕРВЕРА ==========
// Панель отдаётся тем же сервером, что и API, поэтому по умолчанию работаем
// с тем же origin. VITE_API_URL нужен только для `npm run dev`, когда панель
// поднята на отдельном порту Vite.
// ===================================

export const BASE_URL: string = import.meta.env.VITE_API_URL || window.location.origin;

// ========== ТЕКУЩИЙ КЛУБ ==========
// Клуб выбирается в кабинете и приезжает в ссылке /panel/{slug}
// (старые /panel/klub/3 и /panel?club=3 тоже читаем и нормализуем).
// Дальше id живёт в localStorage и уходит на сервер заголовком X-Club-Id.
// ==================================

const CLUB_STORAGE_KEY = 'clubId';
const CLUB_SLUG_STORAGE_KEY = 'clubSlug';
const RESERVED_SEGMENTS = new Set(['assets', 'images', 'klub', 'vite.svg']);

/** Канонический адрес панели клуба по slug. */
export const panelUrlForClub = (slug: string) =>
  `/panel/${encodeURIComponent(slug)}`;

const readLegacyClubIdFromUrl = (): string | null => {
  const pathMatch = window.location.pathname.match(/\/panel\/klub\/(\d+)\/?/i);
  if (pathMatch) return pathMatch[1];
  const fromQuery = new URLSearchParams(window.location.search).get('club');
  return fromQuery && /^\d+$/.test(fromQuery) ? fromQuery : null;
};

const readSlugFromUrl = (): string | null => {
  const match = window.location.pathname.match(/^\/panel\/([^/]+)\/?$/i);
  if (!match) return null;
  const segment = decodeURIComponent(match[1]).trim().toLowerCase();
  if (!segment || RESERVED_SEGMENTS.has(segment) || /^\d+$/.test(segment)) return null;
  if (segment === 'klub') return null;
  return segment;
};

const applyClubContext = (id: string, slug: string) => {
  if (localStorage.getItem(CLUB_STORAGE_KEY) !== id) {
    localStorage.removeItem('authToken');
    localStorage.removeItem('userRole');
  }
  localStorage.setItem(CLUB_STORAGE_KEY, id);
  localStorage.setItem(CLUB_SLUG_STORAGE_KEY, slug);
  const canonical = panelUrlForClub(slug);
  if (window.location.pathname + window.location.search !== canonical) {
    window.history.replaceState(null, '', canonical);
  }
};

/**
 * Резолвит клуб из URL до старта React. Без этого логин персонала
 * не знает, в какую клубную базу стучаться.
 */
export async function ensureClubContext(): Promise<void> {
  const legacyId = readLegacyClubIdFromUrl();
  if (legacyId) {
    try {
      const res = await fetch(`${BASE_URL}/api/clubs/resolve-id/${legacyId}`);
      if (res.ok) {
        const data = await res.json();
        applyClubContext(String(data.id), String(data.slug));
        return;
      }
    } catch {
      /* fall through */
    }
    localStorage.setItem(CLUB_STORAGE_KEY, legacyId);
    return;
  }

  const slug = readSlugFromUrl();
  if (!slug) return;

  try {
    const res = await fetch(`${BASE_URL}/api/clubs/resolve/${encodeURIComponent(slug)}`);
    if (!res.ok) return;
    const data = await res.json();
    applyClubContext(String(data.id), String(data.slug));
  } catch {
    /* панель покажет ошибки API сама */
  }
}

export const getClubId = (): string | null => localStorage.getItem(CLUB_STORAGE_KEY);

export const getClubSlug = (): string | null => localStorage.getItem(CLUB_SLUG_STORAGE_KEY);

export const setClubId = (clubId: number | string) =>
  localStorage.setItem(CLUB_STORAGE_KEY, String(clubId));

export const setClubSlug = (slug: string) =>
  localStorage.setItem(CLUB_SLUG_STORAGE_KEY, slug);

// ========== КТО РАБОТАЕТ В ПАНЕЛИ ==========
// Панель открывают и владелец клуба, и персонал зала. Роли для этого не хватает:
// у владельца внутри клуба тоже права Super. Различает их тип токена: у владельца
// и платформенного админа он account, у персонала — club.
// ===========================================

export const readTokenPayload = (token: string | null): Record<string, any> | null => {
  if (!token) return null;
  try {
    return JSON.parse(atob(token.split('.')[1]));
  } catch {
    return null;
  }
};

/** Владелец клуба или админ платформы: у него есть кабинет, куда можно вернуться. */
export const isAccountSession = (): boolean =>
  readTokenPayload(localStorage.getItem('authToken'))?.token_type === 'account';

/**
 * Адрес хаба. Клуб уезжает в query, потому что WebSocket-рукопожатие
 * не позволяет добавить свой заголовок.
 */
export const getHubUrl = (): string => {
  const clubId = getClubId();
  return clubId ? `${BASE_URL}/clubhub?clubId=${clubId}` : `${BASE_URL}/clubhub`;
};

// Создаем экземпляр axios
const api = axios.create({
  baseURL: `${BASE_URL}/api`, // Базовый путь API
});

// Автоматически добавляем токен авторизации и клуб
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('authToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }

  const clubId = getClubId();
  if (clubId) {
    config.headers['X-Club-Id'] = clubId;
  }
  return config;
});

api.interceptors.response.use((response) => {
  return response;
}, (error) => {
  // 401 от формы логина — это просто неверный пароль, а не протухшая сессия:
  // редиректить в кабинет нельзя, иначе пользователь не увидит ошибку.
  const url: string = error.config?.url || '';
  const isLoginRequest = url.includes('/Auth/login') || url.includes('/account/login');
  if (error.response && error.response.status === 401 && !isLoginRequest) {
    localStorage.removeItem('authToken');
    localStorage.removeItem('userRole');
    window.location.href = '/kabinet';
  }
  return Promise.reject(error);
});

export default api;

// --- ИНТЕРФЕЙСЫ ОБЩИЕ ---

export interface Computer {
  id: number;
  pcName: string;
  nameToDisplay: string;
  groupName: string;
  isOnline: boolean;
  currentUser: string | null;
  sessionEndTime: string | null;
  status: 'Offline' | 'Locked' | 'Active' | 'Error';
  isApproved: boolean;
  lastSeenAt: string | null;
  currentApp?: string | null;
  currentAppTitle?: string | null;
  currentAppSince?: string | null;
  mapX?: number | null;
  mapY?: number | null;
}

export interface ComputerLayoutItem {
  id: number;
  mapX: number;
  mapY: number;
}

export const saveComputerLayout = async (items: ComputerLayoutItem[]) => {
  const res = await api.put('/Admin/computers/layout', { items });
  return res.data;
};

// --- ТАРИФЫ ---

export interface Tariff {
  id: number;
  name: string;
  durationMinutes: number;
  price: number;
  startHour?: number;
  endHour?: number;
  isFixedTime: boolean;
  /** Сгораемый пакет: остаток минут не сохраняется в профиле. */
  isBurnable: boolean;
  feature1: string;
  feature2: string;
}

export const getTariffs = async (): Promise<Tariff[]> => {
  const res = await api.get('/Tariffs');
  return res.data;
};

export const createTariff = async (tariff: Omit<Tariff, 'id'>) => {
  const res = await api.post('/Tariffs', tariff);
  return res.data;
};

export const deleteTariff = async (id: number) => {
  await api.delete(`/Tariffs/${id}`);
};

export const updateTariff = async (id: number, tariff: Partial<Tariff>) => {
  const res = await api.put(`/Tariffs/${id}`, tariff);
  return res.data;
};

// --- ПОЛЬЗОВАТЕЛИ ---

export interface User {
  id: number;
  username: string;
  email?: string;
  balance: number;
  role?: string;
  createdAt: string;
  currentPcName?: string;
  currentPcDisplay?: string;
  groupId?: number | null;
  groupName?: string | null;
  groupColor?: string | null;
}

export interface ClientGroup {
  id: number;
  name: string;
  color: string;
  discountPercent?: number | null;
  sortOrder: number;
  clientsCount?: number;
}

export interface UserProfile {
  id: number;
  username: string;
  email?: string;
  role?: string;
  balance: number;
  remainingMinutes: number;
  totalSpent: number;
  createdAt: string;
  groupId?: number | null;
  groupName?: string | null;
  groupColor?: string | null;
  groupDiscountPercent?: number | null;
  discountPercent: number;
  discountOverride?: number | null;
  nextThreshold: number | null;
  maxDiscountPercent: number;
  /** Сколько филиалов сети видит этот клиент. */
  networkClubs: number;
  currentPcName?: string | null;
  currentPcDisplay?: string | null;
  currentApp?: string | null;
  sessionEndTime?: string | null;
  totalSessions: number;
  sessions: {
    id: number;
    computerName: string;
    startTime: string;
    endTime: string;
    isActive: boolean;
    price: number;
  }[];
  orders: {
    id: number;
    pcName: string;
    totalPrice: number;
    status: string;
    createdAt: string;
    items: { productNameSnapshot: string; quantity: number }[];
  }[];
  logs: {
    id: number;
    adminName: string;
    actionType: string;
    details: string;
    createdAt: string;
  }[];
}

/**
 * Посетители — сетевой список клиентов, а персонал — клубные учётки,
 * которые живут уже в базе зала и управляют панелью администратора.
 */
export const getClients = async (
  search: string = '',
  opts?: { groupId?: number | null; ungrouped?: boolean }
): Promise<User[]> => {
  const params = new URLSearchParams();
  if (search) params.set('search', search);
  if (opts?.ungrouped) params.set('ungrouped', 'true');
  else if (opts?.groupId != null) params.set('groupId', String(opts.groupId));
  const query = params.toString() ? `?${params.toString()}` : '';
  const res = await api.get(`/Clients${query}`);
  return res.data;
};

export const getClientGroups = async (): Promise<ClientGroup[]> => {
  const res = await api.get('/ClientGroups');
  return res.data;
};

export const createClientGroup = async (payload: {
  name: string;
  color?: string;
  discountPercent?: number | null;
  sortOrder?: number;
}): Promise<ClientGroup> => {
  const res = await api.post('/ClientGroups', payload);
  return res.data;
};

export const updateClientGroup = async (
  id: number,
  payload: {
    name: string;
    color?: string;
    discountPercent?: number | null;
    sortOrder?: number;
  }
): Promise<ClientGroup> => {
  const res = await api.put(`/ClientGroups/${id}`, payload);
  return res.data;
};

export const deleteClientGroup = async (id: number) => {
  await api.delete(`/ClientGroups/${id}`);
};

export const getStaff = async (search: string = ''): Promise<User[]> => {
  const query = search ? `?search=${encodeURIComponent(search)}` : '';
  const res = await api.get(`/Users${query}`);
  return res.data;
};

export const createClient = async (username: string, password: string) => {
  await api.post('/Clients/create', { username, password, role: 'Client', email: '', balance: 0 });
};

export const createStaff = async (username: string, password: string, role: string) => {
  await api.post('/Users/create', { username, password, role, email: '', balance: 0 });
};

export const deleteClient = async (id: number) => {
  await api.delete(`/Clients/${id}`);
};

export const deleteStaff = async (id: number) => {
  await api.delete(`/Users/${id}`);
};

export const getUserProfile = async (id: number): Promise<UserProfile> => {
  const res = await api.get(`/Clients/${id}/profile`);
  return res.data;
};

export const topUpUser = async (username: string, amount: number) => {
  await api.post(`/Clients/${username}/topup`, amount, {
    headers: { 'Content-Type': 'application/json' },
  });
  return true;
};

export type AdjustClientWalletPayload = {
  balance?: number;
  remainingMinutes?: number;
  discountPercent?: number;
  groupId?: number | null;
  clearGroup?: boolean;
};

export const adjustClientWallet = async (id: number, payload: AdjustClientWalletPayload) => {
  const res = await api.put(`/Clients/${id}/wallet`, payload);
  return res.data as {
    message: string;
    balance?: number;
    remainingMinutes?: number;
    discountPercent?: number;
    discountOverride?: number | null;
    groupId?: number | null;
    groupName?: string | null;
    groupColor?: string | null;
    totalSpent?: number;
  };
};

// --- ЗАГРУЗКА КАРТИНОК ---

export const uploadImage = async (file: File): Promise<string> => {
  const form = new FormData();
  form.append('file', file);

  // Content-Type axios проставит сам вместе с boundary.
  const res = await api.post('/uploads/image', form);

  return res.data.url as string;
};

// --- НАСТРОЙКИ КЛУБА И ЛОЯЛЬНОСТЬ ---

export interface ClubSettings {
  id: number;
  name: string;
  city?: string | null;
  address?: string | null;
  loyaltyFirstThreshold: number;
  loyaltyStep: number;
  maxDiscountPercent: number;
  requireComputerApproval: boolean;
  enableShop: boolean;
}

export interface LoyaltyClient {
  id: number;
  username: string;
  balance: number;
  totalSpent: number;
  discountPercent: number;
  nextThreshold: number | null;
}

export const getClubSettings = async (): Promise<ClubSettings> => {
  const res = await api.get('/Club/settings');
  return res.data;
};

export const updateClubSettings = async (settings: {
  loyaltyFirstThreshold: number;
  loyaltyStep: number;
  maxDiscountPercent: number;
  requireComputerApproval: boolean;
  enableShop: boolean;
}) => {
  await api.put('/Club/settings', settings);
};

export const getLoyaltyClients = async (): Promise<LoyaltyClient[]> => {
  const res = await api.get('/Club/loyalty-clients');
  return res.data;
};

// --- УПРАВЛЕНИЕ ПК ---

export const getComputers = async (): Promise<Computer[]> => {
  const res = await api.get('/Admin/computers');
  return res.data;
};

export const stopSession = async (pcId: string) => {
  await api.post(`/Admin/stop?pcId=${pcId}`);
};

export const startSession = async (pcId: string, minutes: number) => {
  await api.post(`/Admin/start?pcId=${pcId}&minutes=${minutes}`);
};

export const shutdownPc = async (pcId: string) => {
  await api.post(`/Admin/shutdown?pcId=${pcId}`);
};

export const rebootPc = async (pcId: string) => {
  await api.post(`/Admin/reboot?pcId=${pcId}`);
};

export const renamePc = async (pcId: string, newName: string) => {
  await api.post(`/Admin/rename?pcId=${pcId}&newName=${encodeURIComponent(newName)}`);
};

export const approvePc = async (pcId: string, displayName?: string) => {
  const params = displayName
    ? `?pcId=${pcId}&displayName=${encodeURIComponent(displayName)}`
    : `?pcId=${pcId}`;
  await api.post(`/Admin/approve-computer${params}`);
};

export const deletePc = async (pcId: string) => {
  await api.delete(`/Admin/computer?pcId=${pcId}`);
};

export const getPendingComputers = async () => {
  const res = await api.get('/Admin/pending-computers');
  return res.data;
};

export interface ComputerDetails {
  id: number;
  pcName: string;
  displayName: string;
  groupName: string;
  isOnline: boolean;
  status: string;
  currentUser: string | null;
  sessionEndTime: string | null;
  lastSeenAt: string | null;
  createdAt: string;
  currentApp: string | null;
  currentAppTitle: string | null;
  currentAppSince: string | null;
  ipAddress: string | null;
  macAddress: string | null;
  cpuName: string | null;
  ramTotalMb: number | null;
  ramUsedMb: number | null;
  gpuName: string | null;
  diskInfo: string | null;
  osVersion: string | null;
  systemInfoUpdatedAt: string | null;
}

export const getComputerDetails = async (pcId: string): Promise<ComputerDetails> => {
  const res = await api.get(`/Admin/computer-details?pcId=${pcId}`);
  return res.data;
};

// --- ИГРЫ И ПО (APPS) ---

export interface AppItem {
  id: number;
  title: string;
  exePath: string;
  imageUrl: string;
  category: string; // 'Game' | 'Application'
  genre?: string;
  arguments?: string;
}

export const getApps = async (): Promise<AppItem[]> => {
  const res = await api.get('/Apps');
  return res.data;
};

export const createApp = async (app: Omit<AppItem, 'id'>) => {
  const res = await api.post('/Apps', app);
  return res.data;
};

export const updateApp = async (id: number, app: Omit<AppItem, 'id'>) => {
  await api.put(`/Apps/${id}`, { id, ...app });
};

export const deleteApp = async (id: number) => {
  await api.delete(`/Apps/${id}`);
};

// --- ИСТОРИЯ (LOGS) ---

export interface AdminLog {
  id: number;
  adminName: string;
  actionType: string;
  target: string;
  details: string;
  createdAt: string;
}

export const getLogs = async (type: string = 'All'): Promise<AdminLog[]> => {
  const query = type && type !== 'All' ? `?type=${type}` : '';
  const res = await api.get(`/Admin/logs${query}`);
  return res.data;
};

// --- ТОВАРЫ (МАГАЗИН) ---

export interface Product {
  id: number;
  name: string;
  category: string;
  price: number;
  imageUrl: string;
  isAvailable: boolean;
  stockQty?: number;
}

export interface StockMovement {
  id: number;
  productId?: number;
  productName?: string;
  category?: string;
  delta: number;
  balanceAfter: number;
  kind: string;
  orderId?: number | null;
  reason: string;
  createdBy: string;
  createdAt: string;
}

export const getProducts = async (): Promise<Product[]> => {
  const res = await api.get('/Products');
  return res.data;
};

export const getInventory = async (): Promise<Product[]> => {
  const res = await api.get('/Products/inventory');
  return res.data;
};

export const adjustStock = async (
  productId: number,
  body: { delta?: number; setTo?: number; reason?: string }
) => {
  const res = await api.post(`/Products/${productId}/stock`, body);
  return res.data;
};

export const getProductMovements = async (productId: number, take = 50): Promise<StockMovement[]> => {
  const res = await api.get(`/Products/${productId}/movements`, { params: { take } });
  return res.data;
};

export const getStockMovements = async (take = 100): Promise<StockMovement[]> => {
  const res = await api.get('/Products/movements', { params: { take } });
  return res.data;
};

export const createProduct = async (product: Omit<Product, 'id' | 'isAvailable'>) => {
  const res = await api.post('/Products', product);
  return res.data;
};

export const deleteProduct = async (id: number) => {
  await api.delete(`/Products/${id}`);
};

// --- ЗАКАЗЫ (КУХНЯ) ---

export interface OrderItem {
  name: string;
  quantity: number;
}

export interface Order {
  id: number;
  pcName: string;
  totalPrice: number;
  status: string; // 'New', 'Processing', 'Completed', 'Cancelled'
  time: string;
  items: OrderItem[];
}

export const getOrders = async (tab: 'new' | 'processing' | 'ready' | 'history'): Promise<Order[]> => {
  let url = '/Orders?';
  if (tab === 'new') url += 'status=New';
  else if (tab === 'processing') url += 'status=Processing';
  else if (tab === 'ready') url += 'status=Ready';
  else url += 'active=false';

  const res = await api.get(url);
  return res.data;
};

export const updateOrderStatus = async (orderId: number, status: string) => {
  await api.post(`/Orders/${orderId}/status?status=${status}`);
};

// --- ЧАТ ---

export interface ChatMessage {
  id: number;
  pcName: string;
  message: string;
  isFromAdmin: boolean;
  createdAt: string;
}

export const getChatHistory = async (pcName: string): Promise<ChatMessage[]> => {
  const res = await api.get(`/Chat/${encodeURIComponent(pcName)}`);
  return res.data;
};

export const clearChatHistory = async (pcName: string) => {
  await api.delete(`/Chat/${encodeURIComponent(pcName)}`);
};

// --- БАННЕРЫ ---

export interface Banner {
  id: number;
  title: string;
  imageUrl: string;
  clickUrl: string;
  position: number; // 1 - left, 2 - right
  isActive: boolean;
}

export const getBanners = async (activeOnly: boolean = false): Promise<Banner[]> => {
  const res = await api.get(`/Banners?activeOnly=${activeOnly}`);
  return res.data;
};

export const createBanner = async (banner: Omit<Banner, 'id'>) => {
  const res = await api.post('/Banners', banner);
  return res.data;
};

export const updateBanner = async (id: number, banner: Partial<Banner>) => {
  await api.put(`/Banners/${id}`, banner);
};

export const deleteBanner = async (id: number) => {
  await api.delete(`/Banners/${id}`);
};

// --- МАССОВЫЕ ДЕЙСТВИЯ ---

export const broadcastMessage = async (message: string) => {
  const res = await api.post('/Admin/broadcast', { message });
  return res.data;
};

export const shutdownAll = async () => {
  const res = await api.post('/Admin/shutdown-all');
  return res.data;
};

export const rebootAll = async () => {
  const res = await api.post('/Admin/reboot-all');
  return res.data;
};

export const downloadReport = async (from?: string, to?: string) => {
  const params = new URLSearchParams();
  if (from) params.append('from', from);
  if (to) params.append('to', to);

  const res = await api.get(`/Admin/report?${params.toString()}`, {
    responseType: 'blob',
  });

  const url = window.URL.createObjectURL(new Blob([res.data]));
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', `report_${from || 'today'}.csv`);
  document.body.appendChild(link);
  link.click();
  link.remove();
};

// ——— Профиль панели ———

export interface PanelProfile {
  id: number;
  username?: string;
  displayName?: string;
  email?: string;
  role: string;
  clubsCount?: number;
  kind?: 'staff' | 'account';
  createdAt?: string;
}

export const getStaffProfile = async (): Promise<PanelProfile> => {
  const res = await api.get('/Auth/me');
  return {
    id: res.data.id,
    username: res.data.username,
    email: res.data.email,
    role: res.data.role,
    kind: 'staff',
    createdAt: res.data.createdAt,
  };
};

export const getAccountProfile = async (): Promise<PanelProfile> => {
  const res = await api.get('/account/me');
  return {
    id: res.data.id,
    displayName: res.data.displayName,
    email: res.data.email,
    role: res.data.role,
    clubsCount: res.data.clubsCount,
    kind: 'account',
  };
};

export const changeStaffPassword = async (currentPassword: string, newPassword: string) => {
  await api.post('/Auth/change-password', { currentPassword, newPassword });
};

export const changeAccountPassword = async (currentPassword: string, newPassword: string) => {
  await api.post('/account/change-password', { currentPassword, newPassword });
};

export type StaffShift = {
  id: number;
  userId: number;
  username: string;
  startedAt: string;
  endedAt: string | null;
  endReason: string | null;
  durationMinutes: number;
  isOpen: boolean;
};

export type StaffShiftMine = {
  current: StaffShift | null;
  recent: StaffShift[];
};

export type StaffShiftSummary = {
  startedAt: string;
  endedAt: string;
  durationMinutes: number;
  totalActions: number;
  byType: { type: string; label: string; count: number }[];
  recent: {
    id: number;
    actionType: string;
    label: string;
    target: string;
    details: string;
    createdAt: string;
  }[];
};

export type StaffShiftEnterResult = {
  status: 'active' | 'started' | 'needsConfirm' | 'rotated';
  shift?: StaffShift;
  previous?: StaffShift;
  summary?: StaffShiftSummary;
};

const ACTIVE_SHIFT_KEY = 'activeShiftId';

export const getActiveShiftId = (): number | null => {
  const raw = localStorage.getItem(ACTIVE_SHIFT_KEY);
  if (!raw) return null;
  const n = Number(raw);
  return Number.isFinite(n) ? n : null;
};

export const setActiveShiftId = (id: number | null) => {
  if (id == null) localStorage.removeItem(ACTIVE_SHIFT_KEY);
  else localStorage.setItem(ACTIVE_SHIFT_KEY, String(id));
};

export const getMyStaffShift = async (): Promise<StaffShiftMine> => {
  const res = await api.get('/StaffShifts/mine');
  return res.data;
};

export const enterStaffShift = async (): Promise<StaffShiftEnterResult> => {
  const known = getActiveShiftId();
  const res = await api.post('/StaffShifts/enter', null, {
    params: known != null ? { knownShiftId: known } : undefined,
  });
  return res.data;
};

export const getStaffShiftSummary = async (): Promise<{
  hasOpen: boolean;
  shift?: StaffShift;
  summary?: StaffShiftSummary;
}> => {
  const res = await api.get('/StaffShifts/summary');
  return res.data;
};

export const confirmStaffShiftReauth = async (): Promise<StaffShiftEnterResult> => {
  const res = await api.post('/StaffShifts/confirm-reauth');
  return res.data;
};

export const startStaffShift = async (): Promise<{ shift: StaffShift; alreadyOpen?: boolean; message?: string }> => {
  const res = await api.post('/StaffShifts/start');
  return res.data;
};

export const endStaffShift = async (reason: 'Manual' | 'Logout' = 'Manual') => {
  const res = await api.post(`/StaffShifts/end?reason=${encodeURIComponent(reason)}`);
  return res.data;
};

export const listStaffShifts = async (take = 50): Promise<StaffShift[]> => {
  const res = await api.get('/StaffShifts', { params: { take } });
  return res.data;
};
