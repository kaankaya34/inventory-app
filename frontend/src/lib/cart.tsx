import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from './api'
import { useAuth } from './auth'
import type { Cart, ClientCartLine } from './types'

interface CartState {
  lines: ClientCartLine[]
  total: number
  itemCount: number
}

interface CartContextValue extends CartState {
  refresh: () => Promise<void>
  add: (barcode: string, quantity: number) => Promise<void>
  remove: (reservationId: string) => Promise<void>
}

const EMPTY: CartState = { lines: [], total: 0, itemCount: 0 }
const CartContext = createContext<CartContextValue | null>(null)

// Geri sayım sunucu/istemci saat farkından etkilenmesin diye alındığı andaki yerel saate göre hesaplanır.
const toState = (c: Cart): CartState => ({
  lines: c.lines.map((l) => ({ ...l, localExpiryMs: Date.now() + l.remainingSeconds * 1000 })),
  total: c.total,
  itemCount: c.itemCount,
})

export function CartProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  // Herkes (müşteri, personel, yönetici) alışveriş yapabilir; sepet her kullanıcıya aittir.
  const userId = user?.id
  const [state, setState] = useState<CartState>(EMPTY)

  const refresh = useCallback(async () => {
    if (!userId) {
      setState(EMPTY)
      return
    }
    try {
      setState(toState(await api.cart()))
    } catch {
      // oturum sorunları api katmanında ele alınır
    }
  }, [userId])

  useEffect(() => {
    void refresh()
  }, [refresh])

  // En yakın süre dolumunda sepeti sunucudan yenile (süresi dolan ürünler düşsün).
  useEffect(() => {
    if (state.lines.length === 0) return
    const next = Math.min(...state.lines.map((l) => l.localExpiryMs))
    const t = setTimeout(() => void refresh(), Math.max(0, next - Date.now()) + 500)
    return () => clearTimeout(t)
  }, [state.lines, refresh])

  const value = useMemo<CartContextValue>(
    () => ({
      ...state,
      refresh,
      add: async (barcode, quantity) => setState(toState(await api.addToCart(barcode, quantity))),
      remove: async (reservationId) => setState(toState(await api.removeFromCart(reservationId))),
    }),
    [state, refresh],
  )

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

export function useCart() {
  const ctx = useContext(CartContext)
  if (!ctx) throw new Error('useCart, CartProvider içinde kullanılmalıdır.')
  return ctx
}
