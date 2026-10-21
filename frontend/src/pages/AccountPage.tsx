import { useEffect, useState, type FormEvent } from 'react'
import { AddressForm, emptyAddress } from '../components/AddressForm'
import { api } from '../lib/api'
import { useAuth } from '../lib/auth'
import { useToast } from '../lib/toast'
import type { Address, SavedCard } from '../lib/types'
import { btn, card, input, label } from '../lib/ui'

export default function AccountPage() {
  const { user } = useAuth()
  const toast = useToast()
  const [fullName, setFullName] = useState('')
  const [phone, setPhone] = useState('')
  const [address, setAddress] = useState<Address>(emptyAddress)
  const [cards, setCards] = useState<SavedCard[]>([])
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    Promise.all([api.profile(), api.myCards()])
      .then(([p, c]) => {
        setFullName(p.fullName)
        setPhone(p.phone ?? '')
        setAddress(p.address ?? emptyAddress)
        setCards(c)
      })
      .catch((e) => toast('error', e instanceof Error ? e.message : 'Profil yüklenemedi'))
  }, [toast])

  const save = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    try {
      const hasAddress = Object.values(address).some((v) => v.trim() !== '')
      await api.updateProfile({ fullName, phone: phone || null, address: hasAddress ? address : null })
      toast('success', 'Profiliniz güncellendi')
    } catch (err) {
      toast('error', err instanceof Error ? err.message : 'Kaydedilemedi')
    } finally {
      setBusy(false)
    }
  }

  const removeCard = async (id: string) => {
    try {
      await api.deleteCard(id)
      setCards((c) => c.filter((x) => x.id !== id))
      toast('info', 'Kart silindi')
    } catch (err) {
      toast('error', err instanceof Error ? err.message : 'Kart silinemedi')
    }
  }

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <h1 className="text-xl font-bold">Hesabım</h1>

      <form onSubmit={save} className={`${card} space-y-4`}>
        <h2 className="font-semibold">Profil</h2>
        <div className="grid gap-3 sm:grid-cols-2">
          <div>
            <label className={label}>E-posta</label>
            <input className={`${input} w-full opacity-70`} value={user?.email ?? ''} disabled />
          </div>
          <div>
            <label className={label}>Ad Soyad</label>
            <input className={`${input} w-full`} required value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </div>
          <div>
            <label className={label}>Telefon</label>
            <input className={`${input} w-full`} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
          </div>
        </div>

        <h2 className="pt-2 font-semibold">Varsayılan Teslimat Adresi</h2>
        <AddressForm value={address} onChange={setAddress} required={false} />

        <button className={`${btn} bg-indigo-600 text-white hover:bg-indigo-500`} disabled={busy}>
          {busy ? 'Kaydediliyor…' : 'Kaydet'}
        </button>
        <p className="text-xs text-slate-500 dark:text-slate-400">Telefon ve adres veritabanında şifreli (AES-256-GCM) saklanır.</p>
      </form>

      <section className={`${card} space-y-3`}>
        <h2 className="font-semibold">Kayıtlı Kartlarım</h2>
        {cards.length === 0 ? (
          <p className="text-sm text-slate-500 dark:text-slate-400">Kayıtlı kartınız yok. Ödeme sırasında "Kartımı kaydet" seçeneğini işaretleyebilirsiniz.</p>
        ) : (
          <ul className="space-y-2">
            {cards.map((c) => (
              <li key={c.id} className="flex items-center justify-between rounded-lg border border-slate-200 px-3 py-2 dark:border-slate-800">
                <div>
                  <div className="font-medium">💳 {c.brand} •••• {c.last4}</div>
                  <div className="text-xs text-slate-500 dark:text-slate-400">
                    {c.holderName} · {String(c.expMonth).padStart(2, '0')}/{String(c.expYear).slice(-2)}
                  </div>
                </div>
                <button className="text-sm text-rose-500 underline" onClick={() => removeCard(c.id)}>Sil</button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
