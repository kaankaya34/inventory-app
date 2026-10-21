export type ReservationStatus = 'ACTIVE' | 'CONFIRMED' | 'EXPIRED' | 'CANCELLED'

export interface Product {
  id: number
  name: string
  sku: string
  barcode: string
  price: number
  physicalStock: number
  reservedStock: number
  availableStock: number
  version: number
  updatedAt: string
}

export interface Reservation {
  id: string
  productId: number
  productName: string
  barcode: string
  userId: string
  quantity: number
  status: ReservationStatus
  expiresAt: string
  createdAt: string
  remainingSeconds: number
}

// Geri sayım sunucu/istemci saat farkından etkilenmesin diye, alındığı andaki yerel saate göre hesaplanır.
export interface ClientReservation extends Reservation {
  localExpiryMs: number
}

export interface StockLog {
  id: number
  productId: number
  barcode: string
  actionType: 'RESERVE' | 'CONFIRM_ORDER' | 'EXPIRE_ROLLBACK' | 'CANCEL' | 'MANUAL_RESTOCK'
  quantityChanged: number
  description: string | null
  createdAt: string
}

export interface ReserveResponse {
  reservationId: string
  barcode: string
  productName: string
  quantity: number
  status: ReservationStatus
  expiresAt: string
  remainingSeconds: number
  availableStock: number
}

export interface SimulationItem {
  index: number
  userId: string
  success: boolean
  status: string
  durationMs: number
  reservationId: string | null
}

export interface SimulationReport {
  barcode: string
  productName: string
  requestCount: number
  quantityPerRequest: number
  successCount: number
  failedCount: number
  errorCount: number
  initialAvailableStock: number
  finalAvailableStock: number
  finalReservedStock: number
  oversellingPrevented: boolean
  stockConsistent: boolean
  totalElapsedMs: number
  minMs: number
  avgMs: number
  p95Ms: number
  maxMs: number
  results: SimulationItem[]
}

// ------------------------------------------------------------ mağaza

export interface User {
  id: string
  email: string
  fullName: string
  role: 'CUSTOMER' | 'STAFF' | 'ADMIN'
  permissions: string[]
}

export interface PermissionInfo {
  key: string
  label: string
  description: string
}

export interface AdminUser extends User {
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

export interface AdminOrder {
  id: string
  orderNo: string
  customerEmail: string
  customerName: string
  totalPrice: number
  status: string
  paymentBrand: string
  paymentLast4: string
  city: string | null
  district: string | null
  createdAt: string
  items: OrderItem[]
}

export interface AuthResponse {
  token: string
  expiresAt: string
  user: User
}

export interface Address {
  fullName: string
  phone: string
  city: string
  district: string
  addressLine: string
  postalCode: string
}

export interface Profile {
  email: string
  fullName: string
  phone: string | null
  address: Address | null
  role: User['role']
}

export interface CartLine {
  reservationId: string
  productId: number
  productName: string
  barcode: string
  sku: string
  unitPrice: number
  quantity: number
  lineTotal: number
  expiresAt: string
  remainingSeconds: number
}

export interface Cart {
  lines: CartLine[]
  total: number
  itemCount: number
}

export interface ClientCartLine extends CartLine {
  localExpiryMs: number
}

export interface SavedCard {
  id: string
  brand: string
  last4: string
  holderName: string
  expMonth: number
  expYear: number
}

export interface NewCard {
  number: string
  holderName: string
  expMonth: number
  expYear: number
  cvv: string
}

export interface CheckoutPayload {
  address: Address
  saveAddress: boolean
  savedCardId?: string
  card?: NewCard
  saveCard: boolean
  cvv?: string
}

export interface OrderItem {
  productId: number
  productName: string
  barcode: string
  quantity: number
  lineTotal: number
}

export interface CustomerOrder {
  id: string
  orderNo: string
  totalPrice: number
  status: string
  paymentBrand: string
  paymentLast4: string
  shippingAddress: Address | null
  createdAt: string
  items: OrderItem[]
}
