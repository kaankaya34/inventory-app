import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { useCart } from '../lib/cart'
import { PERM } from '../lib/permissions'
import { btn } from '../lib/ui'

const navClass = ({ isActive }: { isActive: boolean }) =>
  `rounded-lg px-3 py-1.5 text-sm font-medium transition ${
    isActive
      ? 'bg-indigo-600 text-white'
      : 'text-slate-600 hover:bg-slate-200 dark:text-slate-300 dark:hover:bg-slate-800'
  }`

export function Layout() {
  const { user, isAdmin, isStaff, can, logout } = useAuth()
  const { itemCount } = useCart()
  const [dark, setDark] = useState(() => localStorage.getItem('stock-theme') !== 'light')

  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    localStorage.setItem('stock-theme', dark ? 'dark' : 'light')
  }, [dark])

  return (
    <div className="min-h-screen">
      <header className="sticky top-0 z-40 border-b border-slate-200 bg-white/90 backdrop-blur dark:border-slate-800 dark:bg-slate-900/90">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-3 px-4 py-3 md:px-6">
          <Link to="/" className="text-xl font-extrabold tracking-tight">
            ⚡ Flash<span className="text-indigo-500">Shop</span>
          </Link>

          <nav className="flex flex-1 flex-wrap items-center gap-1">
            <NavLink to="/" end className={navClass}>Mağaza</NavLink>
            {user && <NavLink to="/siparislerim" className={navClass}>Siparişlerim</NavLink>}
            {can(PERM.StockView) && <NavLink to="/admin" end className={navClass}>📊 Stok Paneli</NavLink>}
            {can(PERM.OrdersView) && <NavLink to="/admin/siparisler" className={navClass}>📦 Tüm Siparişler</NavLink>}
            {isAdmin && <NavLink to="/admin/kullanicilar" className={navClass}>👥 Hesaplar ve Yetkiler</NavLink>}
          </nav>

          <div className="flex items-center gap-2">
            <Link
              to="/sepet"
              className={`${btn} relative border border-slate-300 hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800`}
            >
              🛒 Sepet
              {itemCount > 0 && (
                <span className="absolute -right-2 -top-2 flex h-5 min-w-5 items-center justify-center rounded-full bg-rose-500 px-1 text-[11px] font-bold text-white">
                  {itemCount}
                </span>
              )}
            </Link>

            {user ? (
              <>
                <Link to="/hesabim" className={`${btn} flex items-center gap-2 border border-slate-300 hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800`}>
                  👤 {user.fullName.split(' ')[0]}
                  {isStaff && (
                    <span className="rounded-full bg-fuchsia-500/15 px-2 py-0.5 text-xs text-fuchsia-600 dark:text-fuchsia-300">
                      {user.role === 'ADMIN' ? 'Yönetici' : 'Personel'}
                    </span>
                  )}
                </Link>
                <button className={`${btn} text-slate-500 hover:bg-slate-200 dark:hover:bg-slate-800`} onClick={logout}>
                  Çıkış
                </button>
              </>
            ) : (
              <>
                <Link to="/giris" className={`${btn} border border-slate-300 hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800`}>
                  Giriş Yap
                </Link>
                <Link to="/kayit" className={`${btn} bg-indigo-600 text-white hover:bg-indigo-500`}>
                  Kayıt Ol
                </Link>
              </>
            )}

            <button
              className={`${btn} border border-slate-300 hover:bg-slate-200 dark:border-slate-700 dark:hover:bg-slate-800`}
              onClick={() => setDark((d) => !d)}
              title="Tema değiştir"
            >
              {dark ? '☀️' : '🌙'}
            </button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-7xl p-4 md:p-6">
        <Outlet />
      </main>

      <footer className="mx-auto max-w-7xl px-4 pb-6 text-center text-xs text-slate-400 md:px-6">
        FlashShop · test ortamı
      </footer>
    </div>
  )
}
