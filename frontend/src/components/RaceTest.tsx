import { useState } from 'react'
import type { Product, SimulationReport } from '../lib/types'
import { btn, card, fmtMs, input } from '../lib/ui'

interface Props {
  products: Product[]
  barcode: string
  onBarcodeChange: (barcode: string) => void
  running: boolean
  report: SimulationReport | null
  onRun: (barcode: string, count: number) => void
}

function Metric({ label, value, tone = '' }: { label: string; value: string; tone?: string }) {
  return (
    <div className="rounded-lg bg-slate-100 px-3 py-2 dark:bg-slate-800/60">
      <div className={`text-lg font-bold tabular-nums ${tone}`}>{value}</div>
      <div className="text-[11px] uppercase tracking-wide text-slate-500 dark:text-slate-400">{label}</div>
    </div>
  )
}

export function RaceTest({ products, barcode, onBarcodeChange, running, report, onRun }: Props) {
  const [count, setCount] = useState(100)
  const product = products.find((p) => p.barcode === barcode)

  return (
    <div className={card}>
      <h2 className="mb-1 font-semibold">🧪 Race Condition (Yük) Testi</h2>
      <p className="mb-3 text-sm text-slate-500 dark:text-slate-400">
        Backend içinden <code>Task.WhenAll</code> ile aynı anda paralel rezervasyon isteği gönderir; stok kadar istek başarılı olmalı, kalanı reddedilmelidir.
      </p>

      <div className="flex flex-wrap items-center gap-2">
        <select className={`${input} min-w-0 flex-1`} value={barcode} onChange={(e) => onBarcodeChange(e.target.value)}>
          {products.map((p) => (
            <option key={p.id} value={p.barcode}>
              {p.name} — stok: {p.availableStock}
            </option>
          ))}
        </select>
        <label className="flex items-center gap-1.5 text-sm text-slate-500 dark:text-slate-400">
          İstek
          <input
            type="number"
            min={1}
            max={500}
            className={`${input} w-20`}
            value={count}
            onChange={(e) => setCount(Math.min(500, Math.max(1, Number(e.target.value) || 1)))}
          />
        </label>
      </div>

      <button
        className={`${btn} mt-3 w-full bg-gradient-to-r from-rose-600 to-orange-500 py-3 text-base font-bold text-white shadow-lg shadow-rose-900/20 hover:brightness-110`}
        disabled={running || !product}
        onClick={() => onRun(barcode, count)}
      >
        {running ? '⏳ İstekler gönderiliyor…' : `🔥 ${count} Eşzamanlı İstek Gönder (Race Condition Testi)`}
      </button>

      {report && (
        <div className="mt-4 space-y-3">
          <div
            className={`rounded-lg border p-3 text-center text-lg font-bold ${
              report.oversellingPrevented && report.stockConsistent
                ? 'border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300'
                : 'border-rose-500/40 bg-rose-500/10 text-rose-600 dark:text-rose-300'
            }`}
          >
            {report.successCount} Başarılı Rezervasyon, {report.failedCount + report.errorCount} Başarısız
            {report.oversellingPrevented ? ' (Overselling Engellendi!)' : ' (OVERSELLING TESPİT EDİLDİ!)'}
            <div className="mt-0.5 text-xs font-normal opacity-80">
              {report.productName} · stok {report.initialAvailableStock} → {report.finalAvailableStock} · rezerve {report.finalReservedStock}
              {report.stockConsistent ? ' · stok tutarlı ✔' : ' · stok TUTARSIZ ✖'}
              {report.errorCount > 0 && ` · ${report.errorCount} hata`}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
            <Metric label="Toplam Süre" value={fmtMs(report.totalElapsedMs)} tone="text-indigo-600 dark:text-indigo-400" />
            <Metric label="Ortalama" value={fmtMs(report.avgMs)} />
            <Metric label="P95" value={fmtMs(report.p95Ms)} />
            <Metric label="En Hızlı" value={fmtMs(report.minMs)} tone="text-emerald-600 dark:text-emerald-400" />
            <Metric label="En Yavaş" value={fmtMs(report.maxMs)} tone="text-rose-500" />
          </div>

          <div>
            <div className="mb-1 flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
              <span>İstek başına yanıt süresi (ms)</span>
              <span className="flex items-center gap-3">
                <span><span className="mr-1 inline-block h-2 w-2 rounded-sm bg-emerald-500" />başarılı ({report.successCount})</span>
                <span><span className="mr-1 inline-block h-2 w-2 rounded-sm bg-rose-500" />reddedildi ({report.failedCount + report.errorCount})</span>
              </span>
            </div>
            <div className="flex h-28 items-end gap-px rounded-lg bg-slate-100 p-2 dark:bg-slate-800/60">
              {report.results.map((r) => (
                <div
                  key={r.index}
                  title={`#${r.index} ${r.userId} — ${r.success ? 'BAŞARILI' : r.status} — ${fmtMs(r.durationMs)}`}
                  className={`min-w-px flex-1 rounded-t-sm ${r.success ? 'bg-emerald-500' : 'bg-rose-500/70'}`}
                  style={{ height: `${Math.max(4, (r.durationMs / (report.maxMs || 1)) * 100)}%` }}
                />
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
