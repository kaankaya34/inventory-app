import { useState, type ChangeEvent, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { btn, card, input, label } from '../lib/ui'

export default function RegisterPage() {
  const { user, register } = useAuth()
  const navigate = useNavigate()
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/'
  const [form, setForm] = useState({ fullName: '', email: '', phone: '', password: '' })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (user) return <Navigate to={from} replace />

  const set = (k: keyof typeof form) => (e: ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value })

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await register(form.email, form.password, form.fullName, form.phone)
      navigate(from, { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Kayıt yapılamadı')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className={`${card} mx-auto max-w-md space-y-4`}>
      <h1 className="text-xl font-bold">Kayıt Ol</h1>
      <div>
        <label className={label}>Ad Soyad</label>
        <input className={`${input} w-full`} required minLength={2} maxLength={100} autoComplete="name" value={form.fullName} onChange={set('fullName')} />
      </div>
      <div>
        <label className={label}>E-posta</label>
        <input className={`${input} w-full`} type="email" required autoComplete="email" value={form.email} onChange={set('email')} />
      </div>
      <div>
        <label className={label}>Telefon (isteğe bağlı)</label>
        <input className={`${input} w-full`} type="tel" autoComplete="tel" placeholder="+90 5xx xxx xx xx" value={form.phone} onChange={set('phone')} />
      </div>
      <div>
        <label className={label}>Parola</label>
        <input className={`${input} w-full`} type="password" required minLength={8} autoComplete="new-password" value={form.password} onChange={set('password')} />
        <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">En az 8 karakter, en az bir harf ve bir rakam.</p>
      </div>
      {error && <p className="rounded-lg bg-rose-500/10 p-2 text-sm text-rose-600 dark:text-rose-300">{error}</p>}
      <button className={`${btn} w-full bg-indigo-600 py-2.5 text-white hover:bg-indigo-500`} disabled={busy}>
        {busy ? 'Kaydediliyor…' : 'Hesap Oluştur'}
      </button>
      <p className="text-center text-sm text-slate-500 dark:text-slate-400">
        Zaten hesabınız var mı? <Link to="/giris" state={{ from }} className="text-indigo-500 underline">Giriş yapın</Link>
      </p>
    </form>
  )
}
