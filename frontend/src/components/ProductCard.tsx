import { useEffect, useRef, useState } from 'react'
import type { Product } from '../lib/types'
import { btn, card, money } from '../lib/ui'

function Stat({ label, value, tone }: { label: string; value: number; tone: string }) {
  const prev = useRef(value)
  const [flash, setFlash] = useState('')

  useEffect(() => {
    if (prev.current === value) return
    setFlash(value > prev.current ? 'flash-up' : 'flash-down')
    prev.current = value
    const t = setTimeout(() => setFlash(''), 800)
    return () => clearTimeout(t)
  }, [value])

  return (
    <div className={`rounded-lg px-2 py-2 text-center ${flash}`}>
      <div className={`text-2xl font-bold tabular-nums ${tone}`}>{value}</div>
      <div className="text-[11px] uppercase tracking-wide text-slate-500 dark:text-slate-400">{label}</div>
    </div>
  )
}

interface Props {
  product: Product
  selected: boolean
  onSelect: (barcode: string) => void
  onReserve: (barcode: string) => void
  onRestock: (barcode: string) => void
  canReserve?: boolean
  canRestock?: boolean
}

export function ProductCard({ product: p, selected, onSelect, onReserve, onRestock, canReserve = true, canRestock = true }: Props) {
  const pct = p.physicalStock === 0 ? 0 : Math.round((p.availableStock / p.physicalStock) * 100)
  const barColor = p.availableStock === 0 ? 'bg-rose-500' : pct < 30 ? 'bg-amber-500' : 'bg-emerald-500'

  return (
    <div className={`${card} ${selected ? 'ring-2 ring-indigo-500' : ''}`}>
      <button type="button" className="block w-full text-left" onClick={() => onSelect(p.barcode)} title="Barkodu simülatöre aktar">
        <div className="flex items-start justify-between gap-2">
          <h3 className="font-semibold leading-tight">{p.name}</h3>
          <span className="whitespace-nowrap text-sm font-medium text-indigo-600 dark:text-indigo-400">{money(p.price)}</span>
        </div>
        <div className="mt-1 font-mono text-xs text-slate-500 dark:text-slate-400">
          ▮▯▮▮ {p.barcode} · {p.sku}
        </div>
      </button>

      <div className="mt-3 grid grid-cols-3 gap-1">
        <Stat label="Fiziksel" value={p.physicalStock} tone="text-slate-700 dark:text-slate-200" />
        <Stat label="Rezerve" value={p.reservedStock} tone="text-amber-600 dark:text-amber-400" />
        <Stat label="Satılabilir" value={p.availableStock} tone="text-emerald-600 dark:text-emerald-400" />
      </div>

      <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800">
        <div className={`h-full transition-all duration-500 ${barColor}`} style={{ width: `${pct}%` }} />
      </div>

      {(canReserve || canRestock) && (
        <div className="mt-3 flex gap-2">
          {canReserve && (
            <button
              className={`${btn} flex-1 bg-indigo-600 text-white hover:bg-indigo-500`}
              disabled={p.availableStock < 1}
              onClick={() => onReserve(p.barcode)}
            >
              1 Adet Rezerve Et
            </button>
          )}
          {canRestock && (
            <button
              className={`${btn} border border-slate-300 hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800 ${canReserve ? '' : 'flex-1'}`}
              onClick={() => onRestock(p.barcode)}
            >
              +10 Stok Ekle
            </button>
          )}
        </div>
      )}
    </div>
  )
}
