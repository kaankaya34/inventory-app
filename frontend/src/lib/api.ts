import type {
  AdminOrder, AdminUser, AuthResponse, Cart, CheckoutPayload, CustomerOrder, PermissionInfo, Product, Profile,
  Reservation, ReserveResponse, SavedCard, SimulationReport, StockLog, User,
} from './types'

export const API_BASE: string = import.meta.env.VITE_API_BASE ?? ''

export class ApiError extends Error {
  status: number
  code: string
  availableStock?: number

  constructor(status: number, code: string, message: string, availableStock?: number) {
    super(message)
    this.status = status
    this.code = code
    this.availableStock = availableStock
  }
}

let authToken: string | null = null
let onUnauthorized: (() => void) | null = null

export const getAuthToken = () => authToken
export const setAuthToken = (t: string | null) => {
  authToken = t
}
export const setUnauthorizedHandler = (fn: (() => void) | null) => {
  onUnauthorized = fn
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE'

async function request<T>(method: Method, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (authToken) headers['Authorization'] = `Bearer ${authToken}`

  let res: Response
  try {
    res = await fetch(`${API_BASE}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, 'NETWORK', 'Sunucuya ulaşılamadı. Backend çalışıyor mu?')
  }

  const text = await res.text()
  const data = text ? JSON.parse(text) : undefined
  if (!res.ok) {
    // Oturum açıkken gelen 401: token süresi dolmuş/geçersiz.
    if (res.status === 401 && authToken && !path.startsWith('/api/auth/')) onUnauthorized?.()
    const message =
      res.status === 429
        ? 'Çok fazla istek gönderildi. Lütfen biraz bekleyin.'
        : res.status === 403
          ? 'Bu işlem için yetkiniz yok.'
          : (data?.message ?? data?.title ?? `İstek başarısız (HTTP ${res.status})`)
    throw new ApiError(res.status, data?.code ?? 'ERROR', message, data?.availableStock ?? undefined)
  }
  return data as T
}

export const api = {
  // herkese açık
  products: () => request<Product[]>('GET', '/api/products'),

  // kimlik doğrulama
  register: (email: string, password: string, fullName: string, phone: string) =>
    request<AuthResponse>('POST', '/api/auth/register', { email, password, fullName, phone: phone || null }),
  login: (email: string, password: string) => request<AuthResponse>('POST', '/api/auth/login', { email, password }),
  me: () => request<User>('GET', '/api/auth/me'),

  // mağaza (müşteri)
  cart: () => request<Cart>('GET', '/api/cart'),
  addToCart: (barcode: string, quantity: number) => request<Cart>('POST', '/api/cart/items', { barcode, quantity }),
  removeFromCart: (reservationId: string) => request<Cart>('DELETE', `/api/cart/items/${reservationId}`),
  checkout: (payload: CheckoutPayload) => request<CustomerOrder>('POST', '/api/checkout', payload),
  myOrders: () => request<CustomerOrder[]>('GET', '/api/my/orders'),
  myCards: () => request<SavedCard[]>('GET', '/api/my/cards'),
  deleteCard: (id: string) => request<void>('DELETE', `/api/my/cards/${id}`),
  profile: () => request<Profile>('GET', '/api/my/profile'),
  updateProfile: (p: Pick<Profile, 'fullName' | 'phone' | 'address'>) => request<Profile>('PUT', '/api/my/profile', p),

  // yönetim
  reservations: (limit = 100) => request<Reservation[]>('GET', `/api/reservations?limit=${limit}`),
  adminPermissions: () => request<PermissionInfo[]>('GET', '/api/admin/permissions'),
  adminUsers: () => request<AdminUser[]>('GET', '/api/admin/users'),
  createUser: (u: { email: string; password: string; fullName: string; role: string; permissions: string[] }) =>
    request<AdminUser>('POST', '/api/admin/users', u),
  updateUser: (id: string, u: { fullName?: string; role?: string; permissions?: string[]; isActive?: boolean }) =>
    request<AdminUser>('PUT', `/api/admin/users/${id}`, u),
  resetPassword: (id: string, newPassword: string) =>
    request<void>('POST', `/api/admin/users/${id}/reset-password`, { newPassword }),
  adminOrders: (limit = 100) => request<AdminOrder[]>('GET', `/api/admin/orders?limit=${limit}`),
  logs: (limit = 50) => request<StockLog[]>('GET', `/api/logs?limit=${limit}`),
  reserve: (barcode: string, quantity: number, userId: string) =>
    request<ReserveResponse>('POST', '/api/inventory/reserve', { barcode, quantity, userId }),
  confirm: (reservationId: string) => request<unknown>('POST', '/api/orders/confirm', { reservationId }),
  cancel: (reservationId: string) => request<unknown>('POST', '/api/inventory/cancel', { reservationId }),
  restock: (barcode: string, addQuantity: number) =>
    request<unknown>('POST', '/api/inventory/restock', { barcode, addQuantity }),
  race: (barcode: string, requestCount: number) =>
    request<SimulationReport>('POST', '/api/simulation/race-condition', { barcode, requestCount, quantity: 1 }),
}
