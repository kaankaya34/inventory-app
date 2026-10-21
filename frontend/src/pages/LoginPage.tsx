import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { btn, card, input, label } from '../lib/ui'

// Müşteri, personel ve yönetici aynı hesapla, aynı sayfadan giriş yapar. Kişinin personel/yönetici yetkisi varsa
// (staff_users kaydı) hem alışveriş yapabilir hem yetkileri kadar yönetim yapabilir.
export default function LoginPage() {
  const { user, isStaff, login } = useAuth()
  const navigate = useNavigate()
  const from = (useLocation().state as { from?: string } | null)?.from
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const target = (staff: boolean) => from ?? (staff ? '/admin' : '/')

  if (user) return <Navigate to={target(isStaff)} replace />

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const signedIn = await login(email, password)
      navigate(target(signedIn.role !== 'CUSTOMER'), { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Giriş yapılamadı')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className={`${card} mx-auto max-w-md space-y-4`}>
      <h1 className="text-xl font-bold">Giriş Yap</h1>
      <div>
        <label className={label}>E-posta</label>
        <input className={`${input} w-full`} type="email" autoComplete="email" required autoFocus value={email} onChange={(e) => setEmail(e.target.value)} />
      </div>
      <div>
        <label className={label}>Parola</label>
        <input className={`${input} w-full`} type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
      </div>
      {error && <p className="rounded-lg bg-rose-500/10 p-2 text-sm text-rose-600 dark:text-rose-300">{error}</p>}
      <button className={`${btn} w-full bg-indigo-600 py-2.5 text-white hover:bg-indigo-500`} disabled={busy}>
        {busy ? 'Giriş yapılıyor…' : 'Giriş Yap'}
      </button>
      <p className="text-center text-sm text-slate-500 dark:text-slate-400">
        Hesabınız yok mu? <Link to="/kayit" state={{ from }} className="text-indigo-500 underline">Kayıt olun</Link>
      </p>
    </form>
  )
}
