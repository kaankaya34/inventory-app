import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AddressForm, emptyAddress } from '../components/AddressForm'
import { ApiError, api } from '../lib/api'
import { useAuth } from '../lib/auth'
import { useCart } from '../lib/cart'
import type { Address, CheckoutPayload, SavedCard } from '../lib/types'
import { btn, card, groupCardNumber, input, label, mmss, money, productEmoji } from '../lib/ui'
import { useNow } from '../lib/useNow'

function parseExpiry(v: string): { month: number; year: number } | null {
  const m = /^(\d{1,2})\s*\/\s*(\d{2}|\d{4})$/.exec(v.trim())
  if (!m) return null
  const year = Number(m[2])
  return { month: Number(m[1]), year: year < 100 ? 2000 + year : year }
}

export default function CheckoutPage() {
  const { user } = useAuth()
  const { lines, total, refresh } = useCart()
  const navigate = useNavigate()
  const now = useNow()

  const [address, setAddress] = useState<Address>(emptyAddress)
  const [saveAddress, setSaveAddress] = useState(false)
  const [cards, setCards] = useState<SavedCard[]>([])
  const [mode, setMode] = useState<'saved' | 'new'>('new')
  const [savedCardId, setSavedCardId] = useState('')
  const [cvv, setCvv] = useState('')
  const [number, setNumber] = useState('')
  const [holder, setHolder] = useState('')
  const [expiry, setExpiry] = useState('')
  const [saveCard, setSaveCard] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([api.profile(), api.myCards()])
      .then(([profile, saved]) => {
        if (cancelled) return
        setAddress(profile.address ?? { ...emptyAddress, fullName: profile.fullName, phone: profile.phone ?? '' })
        setCards(saved)
        if (saved.length > 0) {
          setMode('saved')
          setSavedCardId(saved[0].id)
        }
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [])

  const live = lines.filter((l) => l.localExpiryMs > now)
  const soonest = live.length ? Math.min(...live.map((l) => l.localExpiryMs)) : 0
  const remaining = Math.max(0, Math.ceil((soonest - now) / 1000))

  if (live.length === 0)
    return (
      <div className={`${card} mx-auto max-w-md py-10 text-center`}>
        <div className="text-5xl">🛒</div>
        <p className="mt-3 font-semibold">Sepetiniz boş veya ürünlerin rezervasyon süresi doldu.</p>
        <Link to="/" className={`${btn} mt-4 inline-block bg-indigo-600 text-white hover:bg-indigo-500`}>Mağazaya Dön</Link>
      </div>
    )

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)

    const payload: CheckoutPayload = { address, saveAddress, saveCard: false }
    if (mode === 'saved') {
      payload.savedCardId = savedCardId
      payload.cvv = cvv
    } else {
      const exp = parseExpiry(expiry)
      if (!exp) return setError('Son kullanma tarihini AA/YY biçiminde giriniz.')
      payload.card = { number, holderName: holder, expMonth: exp.month, expYear: exp.year, cvv }
      payload.saveCard = saveCard
    }

    setBusy(true)
    try {
      const order = await api.checkout(payload)
      await refresh()
      navigate('/siparislerim', { replace: true, state: { orderNo: order.orderNo } })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ödeme tamamlanamadı')
      if (err instanceof ApiError && (err.code === 'CART_EXPIRED' || err.code === 'CART_CHANGED')) await refresh()
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="grid gap-4 lg:grid-cols-3">
      <div className="space-y-4 lg:col-span-2">
        <section className={`${card} space-y-3`}>
          <h2 className="font-semibold">1. Teslimat Adresi</h2>
          <AddressForm value={address} onChange={setAddress} />
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={saveAddress} onChange={(e) => setSaveAddress(e.target.checked)} />
            Bu adresi sonraki alışverişler için kaydet
          </label>
        </section>

        <section className={`${card} space-y-3`}>
          <h2 className="font-semibold">2. Ödeme</h2>

          {cards.length > 0 && (
            <div className="flex gap-2">
              <button type="button" className={`${btn} border ${mode === 'saved' ? 'border-indigo-500 bg-indigo-500/10' : 'border-slate-300 dark:border-slate-700'}`} onClick={() => setMode('saved')}>
                Kayıtlı kartım
              </button>
              <button type="button" className={`${btn} border ${mode === 'new' ? 'border-indigo-500 bg-indigo-500/10' : 'border-slate-300 dark:border-slate-700'}`} onClick={() => setMode('new')}>
                Yeni kart
              </button>
            </div>
          )}

          {mode === 'saved' ? (
            <div className="grid gap-3 sm:grid-cols-2">
              <div>
                <label className={label}>Kart</label>
                <select className={`${input} w-full`} value={savedCardId} onChange={(e) => setSavedCardId(e.target.value)}>
                  {cards.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.brand} •••• {c.last4} ({String(c.expMonth).padStart(2, '0')}/{String(c.expYear).slice(-2)})
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className={label}>CVV (güvenlik için her ödemede istenir)</label>
                <input className={`${input} w-full`} required type="password" inputMode="numeric" autoComplete="cc-csc" maxLength={4} value={cvv} onChange={(e) => setCvv(e.target.value.replace(/\D/g, ''))} />
              </div>
            </div>
          ) : (
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="sm:col-span-2">
                <label className={label}>Kart Numarası</label>
                <input className={`${input} w-full font-mono`} required inputMode="numeric" autoComplete="cc-number" placeholder="4242 4242 4242 4242" value={number} onChange={(e) => setNumber(groupCardNumber(e.target.value))} />
              </div>
              <div className="sm:col-span-2">
                <label className={label}>Kart Üzerindeki İsim</label>
                <input className={`${input} w-full uppercase`} required autoComplete="cc-name" value={holder} onChange={(e) => setHolder(e.target.value)} />
              </div>
              <div>
                <label className={label}>Son Kullanma (AA/YY)</label>
                <input className={`${input} w-full font-mono`} required placeholder="12/30" maxLength={7} autoComplete="cc-exp" value={expiry} onChange={(e) => setExpiry(e.target.value)} />
              </div>
              <div>
                <label className={label}>CVV</label>
                <input className={`${input} w-full font-mono`} required type="password" inputMode="numeric" autoComplete="cc-csc" maxLength={4} value={cvv} onChange={(e) => setCvv(e.target.value.replace(/\D/g, ''))} />
              </div>
              <label className="flex items-center gap-2 text-sm sm:col-span-2">
                <input type="checkbox" checked={saveCard} onChange={(e) => setSaveCard(e.target.checked)} />
                Kartımı şifreli olarak kaydet (CVV kaydedilmez)
              </label>
            </div>
          )}

          <div className="rounded-lg bg-slate-100 p-3 text-xs text-slate-600 dark:bg-slate-800/60 dark:text-slate-300">
            🔒 Test ortamıdır, gerçek tahsilat yapılmaz. Test kartları: <code>4242 4242 4242 4242</code> (onay),{' '}
            <code>4000 0000 0000 0002</code> (reddedilir), <code>4000 0000 0000 9995</code> (yetersiz bakiye). Tarih ileri, CVV herhangi 3 hane olabilir.
          </div>
        </section>
      </div>

      <aside className={`${card} h-fit space-y-3`}>
        <h2 className="font-semibold">Sipariş Özeti</h2>
        <ul className="space-y-1.5 text-sm">
          {live.map((l) => (
            <li key={l.reservationId} className="flex justify-between gap-2">
              <span className="truncate">{productEmoji(l.sku)} {l.quantity} × {l.productName}</span>
              <span className="shrink-0">{money(l.lineTotal)}</span>
            </li>
          ))}
        </ul>
        <div className="flex justify-between border-t border-slate-200 pt-3 text-lg font-bold dark:border-slate-800">
          <span>Toplam</span>
          <span>{money(total)}</span>
        </div>
        <div className={`text-center text-xs font-medium ${remaining <= 60 ? 'text-rose-500' : 'text-amber-600 dark:text-amber-400'}`}>
          ⏳ Rezervasyon süresi: <span className="font-mono">{mmss(remaining)}</span> — süre dolarsa ürünler serbest bırakılır
        </div>
        {error && <p className="rounded-lg bg-rose-500/10 p-2 text-sm text-rose-600 dark:text-rose-300">{error}</p>}
        <button className={`${btn} w-full bg-emerald-600 py-3 text-base text-white hover:bg-emerald-500`} disabled={busy}>
          {busy ? 'Ödeme alınıyor…' : `${money(total)} Öde ve Siparişi Tamamla`}
        </button>
        <p className="text-center text-xs text-slate-500 dark:text-slate-400">{user?.email} hesabıyla sipariş veriyorsunuz.</p>
      </aside>
    </form>
  )
}
