import { Navigate, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { RequireAuth } from './components/RequireAuth'
import AccountPage from './pages/AccountPage'
import AdminOrdersPage from './pages/AdminOrdersPage'
import AdminPage from './pages/AdminPage'
import AdminUsersPage from './pages/AdminUsersPage'
import CartPage from './pages/CartPage'
import CheckoutPage from './pages/CheckoutPage'
import LoginPage from './pages/LoginPage'
import OrdersPage from './pages/OrdersPage'
import RegisterPage from './pages/RegisterPage'
import ShopPage from './pages/ShopPage'
import { PERM } from './lib/permissions'

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<ShopPage />} />
        <Route path="giris" element={<LoginPage />} />
        <Route path="kayit" element={<RegisterPage />} />
        <Route path="sepet" element={<RequireAuth><CartPage /></RequireAuth>} />
        <Route path="odeme" element={<RequireAuth><CheckoutPage /></RequireAuth>} />
        <Route path="siparislerim" element={<RequireAuth><OrdersPage /></RequireAuth>} />
        <Route path="hesabim" element={<RequireAuth><AccountPage /></RequireAuth>} />
        <Route path="admin" element={<RequireAuth permission={PERM.StockView}><AdminPage /></RequireAuth>} />
        <Route path="admin/siparisler" element={<RequireAuth permission={PERM.OrdersView}><AdminOrdersPage /></RequireAuth>} />
        <Route path="admin/kullanicilar" element={<RequireAuth admin><AdminUsersPage /></RequireAuth>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
