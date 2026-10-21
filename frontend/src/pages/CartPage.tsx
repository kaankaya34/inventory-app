import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useCart } from '../lib/cart'
import { useToast } from '../lib/toast'
import { btn, card, mmss, money } from '../lib/ui'
import { useNow } from '../lib/useNow'

export default function CartPage() {
  const { lines, total, itemCount, remove } = useCart()
  const toast = useToast()
  const navigate = useNavigate()
  const now = useNow()
  const [removing, setRemoving] = useState<string | null>(null)

  const live = lines.filter((l) => l.localExpiryMs > now)

  const onRemove = async (id: string) => {
    setRemoving(id)
    try {
      await remove(id)
    } catch (e) {
      toast('error', e instanceof Error ? e.message : 'Ürün kaldırılamadı')
    } finally {
      setRemoving(null)
    }
  }

  if (live.length === 0)
    return (
      <div className={`${card} mx-auto max-w-md py-10 text-center`}>
        <h1 className="text-lg font-semibold">Sepetiniz boş</h1>
        <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
          Sepete eklenen ürünler 5 dakika sonra otomatik olarak serbest bırakılır.
        </p>
        <Link to="/" className={`${btn} mt-4 inline-block bg-indigo-600 text-white hover:bg-indigo-500`}>
          Alışverişe Başla
        </Link>
      </div>
    )

  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <div className="space-y-3 lg:col-span-2">
        <h1 className="text-xl font-bold">Sepetim ({itemCount} ürün)</h1>
        {live.map((l) => {
          const remaining = Math.min(l.remainingSeconds, Math.ceil((l.localExpiryMs - now) / 1000))
          return (
            <div key={l.reservationId} className={`${card} flex items-center justify-between gap-4`}>
              <div className="min-w-0 flex-1">
                <div className="truncate font-semibold">{l.productName}</div>
                <div className="text-sm text-slate-500 dark:text-slate-400">
                  {l.quantity} × {money(l.unitPrice)}
                </div>
                <div className={`mt-1 text-xs font-medium ${remaining <= 60 ? 'text-rose-500' : 'text-amber-600 dark:text-amber-400'}`}>
                  Sizin için ayrıldı: <span className="font-mono">{mmss(remaining)}</span>
                </div>
              </div>
              <div className="text-right">
                <div className="font-bold">{money(l.lineTotal)}</div>
                <button
                  className="mt-1 text-xs text-slate-500 underline hover:text-rose-500 disabled:opacity-40"
                  disabled={removing === l.reservationId}
                  onClick={() => onRemove(l.reservationId)}
                >
                  Kaldır
                </button>
              </div>
            </div>
          )
        })}
      </div>

      <aside className={`${card} h-fit space-y-3`}>
        <h2 className="font-semibold">Sipariş Özeti</h2>
        <div className="flex justify-between text-sm">
          <span className="text-slate-500 dark:text-slate-400">Ara toplam</span>
          <span>{money(total)}</span>
        </div>
        <div className="flex justify-between text-sm">
          <span className="text-slate-500 dark:text-slate-400">Kargo</span>
          <span className="text-emerald-600 dark:text-emerald-400">Ücretsiz</span>
        </div>
        <div className="flex justify-between border-t border-slate-200 pt-3 text-lg font-bold dark:border-slate-800">
          <span>Toplam</span>
          <span>{money(total)}</span>
        </div>
        <button className={`${btn} w-full bg-emerald-600 py-3 text-base text-white hover:bg-emerald-500`} onClick={() => navigate('/odeme')}>
          Ödemeye Geç
        </button>
      </aside>
    </div>
  )
}
