import { useEffect, useState } from 'react'
import type { ClientReservation } from '../lib/types'
import { btn, card, mmss, timeOf } from '../lib/ui'

interface Props {
  reservations: ClientReservation[]
  onConfirm: (id: string) => Promise<void>
  onCancel: (id: string) => Promise<void>
  canManage?: boolean
}

function Row({
  r,
  now,
  onConfirm,
  onCancel,
  canManage,
}: { r: ClientReservation; now: number; canManage: boolean } & Pick<Props, 'onConfirm' | 'onCancel'>) {
  const [busy, setBusy] = useState(false)
  // `now` bir önceki tick'ten kalma olabileceği için, alındığı andaki sunucu değerini üst sınır olarak kullan.
  const remaining = Math.min(r.remainingSeconds, Math.ceil((r.localExpiryMs - now) / 1000))
  const live = r.status === 'ACTIVE' && remaining > 0
  const expired = r.status === 'EXPIRED' || (r.status === 'ACTIVE' && remaining <= 0)

  const run = async (fn: (id: string) => Promise<void>) => {
    setBusy(true)
    try {
      await fn(r.id)
    } finally {
      setBusy(false)
    }
  }

  return (
    <li className={`slide-in rounded-lg border p-3 text-sm ${
      live
        ? 'border-amber-300 bg-amber-50 dark:border-amber-500/40 dark:bg-amber-500/5'
        : 'border-slate-200 bg-slate-50 opacity-80 dark:border-slate-800 dark:bg-slate-950/40'
    }`}>
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="truncate font-medium">
            {r.quantity} × {r.productName}
          </div>
          <div className="truncate font-mono text-xs text-slate-500 dark:text-slate-400">
            {r.userId} · {timeOf(r.createdAt)}
          </div>
        </div>

        {live && (
          <div className={`font-mono text-xl font-bold tabular-nums ${remaining <= 60 ? 'text-rose-500' : 'text-amber-600 dark:text-amber-400'}`}>
            {mmss(remaining)}
          </div>
        )}
        {r.status === 'CONFIRMED' && <span className="whitespace-nowrap text-emerald-600 dark:text-emerald-400">Sipariş Onaylandı</span>}
        {r.status === 'CANCELLED' && <span className="whitespace-nowrap text-slate-500">İptal Edildi</span>}
        {expired && <span className="text-right text-rose-500">Süresi Doldu (Stok İade Edildi)</span>}
      </div>

      {live && canManage && (
        <div className="mt-2 flex gap-2">
          <button
            className={`${btn} flex-1 bg-emerald-600 py-1.5 text-white hover:bg-emerald-500`}
            disabled={busy}
            onClick={() => run(onConfirm)}
          >
            Siparişi Onayla
          </button>
          <button
            className={`${btn} flex-1 border border-slate-300 py-1.5 hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800`}
            disabled={busy}
            onClick={() => run(onCancel)}
          >
            İptal Et
          </button>
        </div>
      )}
    </li>
  )
}

export function ReservationList({ reservations, onConfirm, onCancel, canManage = true }: Props) {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 250)
    return () => clearInterval(t)
  }, [])

  const activeCount = reservations.filter((r) => r.status === 'ACTIVE' && r.localExpiryMs > now).length

  return (
    <div className={card}>
      <div className="mb-3 flex items-center justify-between">
        <h2 className="font-semibold">Aktif Rezervasyonlar</h2>
        <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700 dark:bg-amber-500/15 dark:text-amber-300">
          {activeCount} aktif
        </span>
      </div>

      {reservations.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-500">Henüz rezervasyon yok.</p>
      ) : (
        <ul className="max-h-[420px] space-y-2 overflow-y-auto pr-1">
          {reservations.map((r) => (
            <Row key={r.id} r={r} now={now} onConfirm={onConfirm} onCancel={onCancel} canManage={canManage} />
          ))}
        </ul>
      )}
    </div>
  )
}
