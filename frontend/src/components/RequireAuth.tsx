import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { card } from '../lib/ui'

// Giriş gerektirir; `permission` verilirse o yetki, `admin` verilirse ADMIN rolü gerekir. Asıl denetim sunucudadır.
export function RequireAuth({
  children,
  admin = false,
  permission,
}: {
  children: ReactNode
  admin?: boolean
  permission?: string
}) {
  const { user, isAdmin, can } = useAuth()
  const location = useLocation()

  if (!user) return <Navigate to="/giris" replace state={{ from: location.pathname }} />

  const allowed = (!admin || isAdmin) && (!permission || can(permission))
  if (!allowed)
    return (
      <div className={`${card} mx-auto max-w-md text-center`}>
        <div className="text-3xl">🔒</div>
        <p className="mt-2 font-semibold">Bu sayfa için yetkiniz yok.</p>
        <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">Yetki için yöneticiyle iletişime geçin.</p>
      </div>
    )
  return <>{children}</>
}
