import { useRef } from 'react'
import type { Product } from '../lib/types'
import { btn, card, input } from '../lib/ui'

interface Props {
  value: string
  onChange: (v: string) => void
  onSubmit: (barcode: string) => void
  products: Product[]
  sessionId: string
}

export function BarcodeSimulator({ value, onChange, onSubmit, products, sessionId }: Props) {
  const ref = useRef<HTMLInputElement>(null)

  const submit = () => {
    const code = value.trim()
    if (!code) return
    onSubmit(code)
    onChange('')
    ref.current?.focus()
  }

  return (
    <div className={card}>
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">📟 Barkod Simülatörü (El Terminali)</h2>
        <span className="text-xs text-slate-500 dark:text-slate-400">
          Oturum: <span className="font-mono">{sessionId}</span>
        </span>
      </div>

      <div className="flex gap-2">
        <input
          ref={ref}
          autoFocus
          className={`${input} flex-1 font-mono text-base`}
          placeholder="Barkodu okutun veya yazın, Enter ile 1 adet rezerve edin…"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') submit()
          }}
        />
        <button className={`${btn} bg-indigo-600 text-white hover:bg-indigo-500`} disabled={!value.trim()} onClick={submit}>
          Rezerve Et ⏎
        </button>
      </div>

      <div className="mt-2 flex flex-wrap gap-1.5">
        {products.map((p) => (
          <button
            key={p.id}
            type="button"
            onClick={() => {
              onChange(p.barcode)
              ref.current?.focus()
            }}
            className="rounded-full border border-slate-300 px-2.5 py-1 font-mono text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
            title={p.name}
          >
            {p.barcode}
          </button>
        ))}
      </div>
    </div>
  )
}
