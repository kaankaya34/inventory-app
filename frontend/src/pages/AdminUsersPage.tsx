import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { api } from '../lib/api'
import { useAuth } from '../lib/auth'
import { PERM, ROLE_LABEL } from '../lib/permissions'
import { useToast } from '../lib/toast'
import type { AdminUser, PermissionInfo } from '../lib/types'
import { btn, card, dateTimeOf, input, label } from '../lib/ui'

const PRESETS: { name: string; perms: string[] }[] = [
  { name: 'Depo görevlisi', perms: [PERM.StockView, PERM.StockRestock] },
  { name: 'Terminal / kasiyer', perms: [PERM.StockView, PERM.ReservationsManage] },
  { name: 'Müşteri temsilcisi', perms: [PERM.OrdersView] },
]

const ROLE_STYLE: Record<string, string> = {
  ADMIN: 'bg-fuchsia-500/15 text-fuchsia-600 dark:text-fuchsia-300',
  STAFF: 'bg-sky-500/15 text-sky-600 dark:text-sky-300',
  CUSTOMER: 'bg-slate-500/15 text-slate-600 dark:text-slate-300',
}

const outlineBtn = `${btn} border border-slate-300 py-1.5 hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800`

function PermissionPicker({
  catalog,
  value,
  onChange,
}: {
  catalog: PermissionInfo[]
  value: string[]
  onChange: (v: string[]) => void
}) {
  const toggle = (key: string) => onChange(value.includes(key) ? value.filter((k) => k !== key) : [...value, key])

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-1.5 text-xs">
        <span className="text-slate-500 dark:text-slate-400">Hazır şablon:</span>
        {PRESETS.map((p) => (
          <button
            key={p.name}
            type="button"
            className="rounded-full border border-slate-300 px-2.5 py-1 hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
            onClick={() => onChange(p.perms)}
          >
            {p.name}
          </button>
        ))}
        <button type="button" className="text-slate-500 underline" onClick={() => onChange(catalog.map((c) => c.key))}>tümünü seç</button>
        <button type="button" className="text-slate-500 underline" onClick={() => onChange([])}>temizle</button>
      </div>
      <div className="grid gap-2 sm:grid-cols-2">
        {catalog.map((p) => (
          <label
            key={p.key}
            className={`flex cursor-pointer gap-2 rounded-lg border p-2.5 text-sm ${
              value.includes(p.key) ? 'border-indigo-500 bg-indigo-500/5' : 'border-slate-200 dark:border-slate-800'
            }`}
          >
            <input type="checkbox" className="mt-1" checked={value.includes(p.key)} onChange={() => toggle(p.key)} />
            <span>
              <span className="font-medium">{p.label}</span>
              <span className="block text-xs text-slate-500 dark:text-slate-400">{p.description}</span>
            </span>
          </label>
        ))}
      </div>
    </div>
  )
}

function RoleSelect({ value, onChange, disabled }: { value: string; onChange: (v: AdminUser['role']) => void; disabled?: boolean }) {
  return (
    <select className={`${input} w-full`} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value as AdminUser['role'])}>
      <option value="CUSTOMER">Müşteri — yönetim yetkisi yok</option>
      <option value="STAFF">Personel — seçtiğiniz yetkiler</option>
      <option value="ADMIN">Yönetici — tam erişim</option>
    </select>
  )
}

function CreateUserForm({ catalog, onCreated }: { catalog: PermissionInfo[]; onCreated: (u: AdminUser) => void }) {
  const toast = useToast()
  const [open, setOpen] = useState(false)
  const [form, setForm] = useState({ fullName: '', email: '', password: '', role: 'STAFF' })
  const [perms, setPerms] = useState<string[]>([])
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    try {
      const created = await api.createUser({ ...form, permissions: form.role === 'STAFF' ? perms : [] })
      toast('success', `${created.email} hesabı oluşturuldu`)
      onCreated(created)
      setForm({ fullName: '', email: '', password: '', role: 'STAFF' })
      setPerms([])
      setOpen(false)
    } catch (err) {
      toast('error', err instanceof Error ? err.message : 'Hesap oluşturulamadı')
    } finally {
      setBusy(false)
    }
  }

  if (!open)
    return (
      <button className={`${btn} bg-indigo-600 text-white hover:bg-indigo-500`} onClick={() => setOpen(true)}>
        Yeni Hesap Oluştur
      </button>
    )

  return (
    <form onSubmit={submit} className={`${card} space-y-3`}>
      <div className="flex items-center justify-between">
        <h2 className="font-semibold">Yeni Hesap</h2>
        <button type="button" className="text-sm text-slate-500 underline" onClick={() => setOpen(false)}>Kapat</button>
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        <div>
          <label className={label}>Ad Soyad</label>
          <input className={`${input} w-full`} required value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
        </div>
        <div>
          <label className={label}>E-posta</label>
          <input className={`${input} w-full`} type="email" required value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
        </div>
        <div>
          <label className={label}>Geçici Parola</label>
          <input className={`${input} w-full`} type="text" required minLength={8} autoComplete="off" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
        </div>
        <div>
          <label className={label}>Rol</label>
          <RoleSelect value={form.role} onChange={(role) => setForm({ ...form, role })} />
        </div>
      </div>

      {form.role === 'STAFF' && <PermissionPicker catalog={catalog} value={perms} onChange={setPerms} />}
      {form.role === 'ADMIN' && (
        <p className="rounded-lg bg-fuchsia-500/10 p-2 text-sm text-fuchsia-700 dark:text-fuchsia-300">
          Yönetici tüm yetkilere sahiptir (hesap yönetimi dahil). Yalnızca güvendiğiniz kişilere verin.
        </p>
      )}

      <button className={`${btn} bg-indigo-600 text-white hover:bg-indigo-500`} disabled={busy}>
        {busy ? 'Oluşturuluyor…' : 'Hesabı Oluştur'}
      </button>
    </form>
  )
}

function UserRow({
  user,
  catalog,
  isSelf,
  onChanged,
}: {
  user: AdminUser
  catalog: PermissionInfo[]
  isSelf: boolean
  onChanged: (u: AdminUser) => void
}) {
  const toast = useToast()
  const [open, setOpen] = useState(false)
  const [role, setRole] = useState(user.role)
  const [perms, setPerms] = useState(user.permissions)
  const [busy, setBusy] = useState(false)
  const labels = new Map(catalog.map((c) => [c.key, c.label]))
  const isStaff = user.role !== 'CUSTOMER'

  const save = async (patch: { role?: string; permissions?: string[]; isActive?: boolean }, okText: string) => {
    setBusy(true)
    try {
      onChanged(await api.updateUser(user.id, patch))
      toast('success', okText)
      return true
    } catch (err) {
      toast('error', err instanceof Error ? err.message : 'Güncellenemedi')
      return false
    } finally {
      setBusy(false)
    }
  }

  const resetPassword = async () => {
    const pw = window.prompt(`${user.email} için yeni parola (en az 8 karakter, harf + rakam):`)
    if (!pw) return
    try {
      await api.resetPassword(user.id, pw)
      toast('success', 'Parola sıfırlandı (hesap kilidi de kaldırıldı)')
    } catch (err) {
      toast('error', err instanceof Error ? err.message : 'Parola sıfırlanamadı')
    }
  }

  return (
    <li className={`${card} space-y-3 ${user.isActive ? '' : 'opacity-60'}`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-semibold">{user.fullName}</span>
            {/* Herkes aynı zamanda müşteridir; yetkisi varsa ayrıca personel/yönetici rozeti */}
            <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${ROLE_STYLE.CUSTOMER}`}>Müşteri</span>
            {isStaff && <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${ROLE_STYLE[user.role]}`}>{ROLE_LABEL[user.role]}</span>}
            {!user.isActive && <span className="rounded-full bg-rose-500/15 px-2 py-0.5 text-xs font-medium text-rose-500">Devre dışı</span>}
            {isSelf && <span className="text-xs text-slate-500">(siz)</span>}
          </div>
          <div className="text-sm text-slate-500 dark:text-slate-400">
            {user.email} · kayıt: {dateTimeOf(user.createdAt)} · son giriş: {user.lastLoginAt ? dateTimeOf(user.lastLoginAt) : '—'}
          </div>
        </div>
        <div className="flex flex-wrap gap-2 text-sm">
          <button className={`${btn} border border-indigo-500/60 py-1.5 text-indigo-600 hover:bg-indigo-500/10 dark:text-indigo-300`} onClick={() => setOpen((o) => !o)}>
            {open ? 'Kapat' : isStaff ? 'Yetkileri düzenle' : 'Yetki ver'}
          </button>
          <button className={outlineBtn} onClick={resetPassword}>Parola sıfırla</button>
        </div>
      </div>

      {isStaff && (
        <div className="flex flex-wrap gap-1.5 text-xs">
          {user.role === 'ADMIN' && <span className="text-slate-500 dark:text-slate-400">Tüm yetkiler (tam erişim)</span>}
          {user.role === 'STAFF' && user.permissions.length === 0 && <span className="text-amber-600 dark:text-amber-400">Henüz yetki verilmedi</span>}
          {user.role === 'STAFF' &&
            user.permissions.map((p) => (
              <span key={p} className="rounded-full bg-indigo-500/10 px-2 py-0.5 text-indigo-600 dark:text-indigo-300">{labels.get(p) ?? p}</span>
            ))}
        </div>
      )}

      {open && (
        <div className="space-y-3 border-t border-slate-200 pt-3 dark:border-slate-800">
          <div className="grid gap-3 sm:grid-cols-2">
            <div>
              <label className={label}>Rol</label>
              <RoleSelect value={role} disabled={isSelf} onChange={setRole} />
              {isSelf && <p className="mt-1 text-xs text-slate-500">Kendi rolünüzü değiştiremezsiniz.</p>}
            </div>
          </div>

          {role === 'STAFF' && <PermissionPicker catalog={catalog} value={perms} onChange={setPerms} />}

          <div className="flex flex-wrap gap-2">
            <button
              className={`${btn} bg-indigo-600 text-white hover:bg-indigo-500`}
              disabled={busy}
              onClick={async () => {
                if (await save({ role, permissions: role === 'STAFF' ? perms : [] }, 'Yetkiler güncellendi (anında geçerli)')) setOpen(false)
              }}
            >
              Kaydet
            </button>
            {!isSelf && (
              <button
                className={`${btn} border ${user.isActive ? 'border-rose-500/50 text-rose-500' : 'border-emerald-500/50 text-emerald-600'}`}
                disabled={busy}
                onClick={() => save({ isActive: !user.isActive }, user.isActive ? 'Hesap devre dışı bırakıldı' : 'Hesap yeniden etkinleştirildi')}
              >
                {user.isActive ? 'Hesabı devre dışı bırak' : 'Hesabı etkinleştir'}
              </button>
            )}
          </div>
        </div>
      )}
    </li>
  )
}

// Yalnızca ADMIN. Herkes `users` tablosunda tek hesaba sahiptir; personel/yönetici yetkileri ayrı `staff_users`
// tablosundadır. Rol/yetki değişiklikleri mevcut oturumlarda da anında geçerli olur.
export default function AdminUsersPage() {
  const { user: me } = useAuth()
  const toast = useToast()
  const [users, setUsers] = useState<AdminUser[]>([])
  const [catalog, setCatalog] = useState<PermissionInfo[]>([])
  const [filter, setFilter] = useState<'ALL' | AdminUser['role']>('ALL')
  const [query, setQuery] = useState('')

  const load = useCallback(async () => {
    try {
      const [u, p] = await Promise.all([api.adminUsers(), api.adminPermissions()])
      setUsers(u)
      setCatalog(p)
    } catch (e) {
      toast('error', e instanceof Error ? e.message : 'Hesaplar yüklenemedi')
    }
  }, [toast])

  useEffect(() => {
    void load()
  }, [load])

  const upsert = (u: AdminUser) =>
    setUsers((prev) => (prev.some((x) => x.id === u.id) ? prev.map((x) => (x.id === u.id ? u : x)) : [u, ...prev]))

  const q = query.trim().toLowerCase()
  const shown = users.filter(
    (u) => (filter === 'ALL' || u.role === filter) && (!q || u.email.toLowerCase().includes(q) || u.fullName.toLowerCase().includes(q)),
  )
  const count = (r: AdminUser['role']) => users.filter((u) => u.role === r).length

  const chip = (value: typeof filter, text: string) => (
    <button
      key={value}
      className={`${btn} py-1.5 ${filter === value ? 'bg-indigo-600 text-white' : 'border border-slate-300 hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800'}`}
      onClick={() => setFilter(value)}
    >
      {text}
    </button>
  )

  return (
    <div className="mx-auto max-w-4xl space-y-4">
      <header>
        <h1 className="text-2xl font-bold">Hesaplar ve Yetkiler</h1>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Herkes tek hesapla (tek e-posta, tek parola) giriş yapar ve her zaman <b>müşteri</b>dir. Ona ek olarak <b>personel</b> (verdiğiniz yetkiler) veya{' '}
          <b>yönetici</b> (tam erişim) yapabilirsiniz. Yetkiler ayrı bir tabloda tutulur; yetkiyi kaldırınca kişi yine müşteri olarak kalır.
          Hesap yönetimi devredilemez; yalnızca yöneticiler yapabilir.
        </p>
      </header>

      <CreateUserForm catalog={catalog} onCreated={upsert} />

      <div className="flex flex-wrap items-center gap-2">
        {chip('ALL', `Hepsi (${users.length})`)}
        {chip('ADMIN', `Yönetici (${count('ADMIN')})`)}
        {chip('STAFF', `Personel (${count('STAFF')})`)}
        {chip('CUSTOMER', `Yalnızca müşteri (${count('CUSTOMER')})`)}
        <input className={`${input} ml-auto w-56`} placeholder="Ad veya e-posta ara…" value={query} onChange={(e) => setQuery(e.target.value)} />
      </div>

      <ul className="space-y-3">
        {shown.length === 0 && <p className="text-sm text-slate-500">Kayıt bulunamadı.</p>}
        {shown.map((u) => (
          <UserRow key={`${u.id}-${u.role}-${u.permissions.join()}`} user={u} catalog={catalog} isSelf={u.id === me?.id} onChanged={upsert} />
        ))}
      </ul>
    </div>
  )
}
