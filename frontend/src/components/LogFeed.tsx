import type { Product, StockLog } from '../lib/types'
import { card, timeOf } from '../lib/ui'

const META: Record<StockLog['actionType'], { icon: string; label: string; tone: string }> = {
  RESERVE: { icon: '🛒', label: 'Rezerve', tone: 'text-amber-600 dark:text-amber-400' },
  CONFIRM_ORDER: { icon: '✅', label: 'Sipariş', tone: 'text-emerald-600 dark:text-emerald-400' },
  EXPIRE_ROLLBACK: { icon: '⏰', label: 'Süre Doldu', tone: 'text-rose-500' },
  CANCEL: { icon: '↩', label: 'İptal', tone: 'text-slate-500 dark:text-slate-400' },
  MANUAL_RESTOCK: { icon: '📦', label: 'Stok Ekleme', tone: 'text-sky-600 dark:text-sky-400' },
}

export function LogFeed({ logs, products }: { logs: StockLog[]; products: Product[] }) {
  const names = new Map(products.map((p) => [p.id, p.name]))

  return (
    <div className={card}>
      <div className="mb-3 flex items-center justify-between">
        <h2 className="font-semibold">📜 Canlı Stok Hareketleri (Audit Trail)</h2>
        <span className="flex items-center gap-1.5 text-xs text-slate-500">
          <span className="h-2 w-2 animate-pulse rounded-full bg-emerald-500" /> canlı
        </span>
      </div>

      {logs.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-500">Henüz hareket yok.</p>
      ) : (
        <ul className="max-h-[420px] space-y-1.5 overflow-y-auto pr-1 text-sm">
          {logs.map((l) => {
            const m = META[l.actionType]
            return (
              <li key={l.id} className="slide-in flex items-start gap-2 rounded-lg border border-slate-200 px-2.5 py-1.5 dark:border-slate-800">
                <span>{m.icon}</span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-baseline justify-between gap-2">
                    <span className={`font-medium ${m.tone}`}>
                      {m.label}{' '}
                      <span className="font-mono">{l.quantityChanged > 0 ? `+${l.quantityChanged}` : l.quantityChanged}</span>
                    </span>
                    <span className="font-mono text-xs text-slate-400">#{l.id} · {timeOf(l.createdAt)}</span>
                  </div>
                  <div className="truncate text-xs text-slate-500 dark:text-slate-400" title={l.description ?? ''}>
                    {names.get(l.productId) ?? l.barcode} — {l.description}
                  </div>
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
