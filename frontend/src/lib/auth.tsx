import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, setAuthToken, setUnauthorizedHandler } from './api'
import type { AuthResponse, User } from './types'

const KEY = 'shop-auth'

interface Stored {
  token: string
  expiresAt: string
  user: User
}

interface AuthContextValue {
  user: User | null
  isAdmin: boolean
  // Personel/yönetici hesabı (müşteri değil). Müşteriler ve personel ayrı tablolarda, ayrı girişlerle tutulur.
  isStaff: boolean
  // Yetki kontrolü yalnızca arayüzü şekillendirir; asıl denetim sunucudadır.
  can: (permission: string) => boolean
  login: (email: string, password: string) => Promise<User>
  register: (email: string, password: string, fullName: string, phone: string) => Promise<User>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

function loadStored(): Stored | null {
  try {
    const raw = localStorage.getItem(KEY)
    if (!raw) return null
    const s = JSON.parse(raw) as Stored
    if (new Date(s.expiresAt).getTime() <= Date.now()) {
      localStorage.removeItem(KEY)
      return null
    }
    return s
  } catch {
    return null
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(() => {
    const s = loadStored()
    setAuthToken(s?.token ?? null)
    return s?.user ?? null
  })

  const logout = useCallback(() => {
    localStorage.removeItem(KEY)
    setAuthToken(null)
    setUser(null)
  }, [])

  const accept = useCallback((r: AuthResponse) => {
    localStorage.setItem(KEY, JSON.stringify({ token: r.token, expiresAt: r.expiresAt, user: r.user } satisfies Stored))
    setAuthToken(r.token)
    setUser(r.user)
    return r.user
  }, [])

  useEffect(() => {
    setUnauthorizedHandler(logout)
    return () => setUnauthorizedHandler(null)
  }, [logout])

  // Saklı oturum hâlâ geçerli mi, rol/yetkiler değişti mi? (yönetici yetkileri değiştirmiş olabilir)
  useEffect(() => {
    const stored = loadStored()
    if (!stored) return
    api
      .me()
      .then((fresh) => {
        localStorage.setItem(KEY, JSON.stringify({ ...stored, user: fresh } satisfies Stored))
        setUser(fresh)
      })
      .catch(() => undefined)
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAdmin: user?.role === 'ADMIN',
      isStaff: user != null && user.role !== 'CUSTOMER',
      can: (permission) => (user?.permissions ?? []).includes(permission),
      login: async (email, password) => accept(await api.login(email, password)),
      register: async (email, password, fullName, phone) => accept(await api.register(email, password, fullName, phone)),
      logout,
    }),
    [user, accept, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth, AuthProvider içinde kullanılmalıdır.')
  return ctx
}
