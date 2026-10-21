import { useCallback, useEffect, useState } from 'react'
import { ApiError, api } from '../lib/api'
import { useAuth } from '../lib/auth'
import { PERM } from '../lib/permissions'
import type { ClientReservation, Product, Reservation, SimulationReport, StockLog } from '../lib/types'
import { useStockHub, type HubStatus } from '../lib/useStockHub'
import { useToast } from '../lib/toast'
import { BarcodeSimulator } from '../components/BarcodeSimulator'
import { LogFeed } from '../components/LogFeed'
import { ProductCard } from '../components/ProductCard'
import { RaceTest } from '../components/RaceTest'
import { ReservationList } from '../components/ReservationList'

const MAX_RESERVATIONS = 100
const MAX_LOGS = 100

const toClient = (r: Reservation): ClientReservation => ({
  ...r,
  localExpiryMs: Date.now() + r.remainingSeconds * 1000,
})

const byCreatedDesc = (a: ClientReservation, b: ClientReservation) =>
  b.createdAt.localeCompare(a.createdAt) || a.id.localeCompare(b.id)

function getSessionId() {
  let id = localStorage.getItem('stock-session-id')
  if (!id) {
    id = `WEB-${Math.random().toString(36).slice(2, 8).toUpperCase()}`
    localStorage.setItem('stock-session-id', id)
  }
  return id
}

const STATUS_UI: Record<HubStatus, { text: string; dot: string }> = {
  connected: { text: 'Canlı bağlantı', dot: 'bg-emerald-500' },
  connecting: { text: 'Bağlanıyor…', dot: 'bg-amber-500 animate-pulse' },
  reconnecting: { text: 'Yeniden bağlanıyor…', dot: 'bg-amber-500 animate-pulse' },
  disconnected: { text: 'Bağlantı yok', dot: 'bg-rose-500' },
}

// Yönetim paneli: canlı stok, el terminali simülasyonu, rezervasyonlar, yük testi ve audit trail (yalnızca ADMIN).
export default function AdminPage() {
  const toast = useToast()
  const { can } = useAuth()
  const canReserve = can(PERM.ReservationsManage)
  const canRestock = can(PERM.StockRestock)
  const canSimulate = can(PERM.SimulationRun)
  const [products, setProducts] = useState<Product[]>([])
  const [reservations, setReservations] = useState<ClientReservation[]>([])
  const [logs, setLogs] = useState<StockLog[]>([])
  const [scan, setScan] = useState('')
  const [raceBarcode, setRaceBarcode] = useState('')
  const [raceRunning, setRaceRunning] = useState(false)
  const [report, setReport] = useState<SimulationReport | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [sessionId] = useState(getSessionId)

  const upsertProduct = useCallback((p: Product) => {
    setProducts((prev) => {
      const existing = prev.find((x) => x.id === p.id)
      if (existing && existing.version > p.version) return prev // sıra dışı gelen eski olay
      const next = existing ? prev.map((x) => (x.id === p.id ? p : x)) : [...prev, p]
      return next.sort((a, b) => a.id - b.id)
    })
  }, [])

  const upsertReservation = useCallback((r: Reservation) => {
    setReservations((prev) => {
      const existing = prev.find((x) => x.id === r.id)
      if (existing && existing.status !== 'ACTIVE' && r.status === 'ACTIVE') return prev
      const next = existing ? prev.map((x) => (x.id === r.id ? toClient(r) : x)) : [toClient(r), ...prev]
      return next.sort(byCreatedDesc).slice(0, MAX_RESERVATIONS)
    })
  }, [])

  const addLog = useCallback((l: StockLog) => {
    setLogs((prev) => (prev.some((x) => x.id === l.id) ? prev : [l, ...prev].sort((a, b) => b.id - a.id).slice(0, MAX_LOGS)))
  }, [])

  const loadAll = useCallback(async () => {
    try {
      const [p, r, l] = await Promise.all([api.products(), api.reservations(MAX_RESERVATIONS), api.logs(50)])
      setProducts(p)
      setReservations(r.map(toClient).sort(byCreatedDesc))
      setLogs(l)
      setLoadError(null)
    } catch (e) {
      setLoadError(e instanceof Error ? e.message : 'Veriler yüklenemedi.')
    }
  }, [])

  const status = useStockHub({
    onProduct: upsertProduct,
    onReservation: upsertReservation,
    onLog: addLog,
    onConnected: () => void loadAll(),
  })

  // Sayfa yüklenirken SignalR bağlanana kadar da veri görünsün.
  useEffect(() => {
    void loadAll()
  }, [loadAll])

  const fail = useCallback(
    (e: unknown) => {
      if (e instanceof ApiError && e.code === 'INSUFFICIENT_STOCK') toast('error', `Yetersiz stok (kalan: ${e.availableStock ?? 0})`)
      else toast('error', e instanceof Error ? e.message : 'Beklenmeyen hata')
    },
    [toast],
  )

  const refreshIfOffline = useCallback(() => {
    if (status !== 'connected') void loadAll()
  }, [status, loadAll])

  const reserve = useCallback(
    async (barcode: string) => {
      try {
        const r = await api.reserve(barcode, 1, sessionId)
        toast('success', `${r.productName}: 1 adet rezerve edildi (satılabilir: ${r.availableStock})`)
      } catch (e) {
        fail(e)
      }
      refreshIfOffline()
    },
    [sessionId, toast, fail, refreshIfOffline],
  )

  const restock = useCallback(
    async (barcode: string) => {
      try {
        await api.restock(barcode, 10)
        toast('info', '+10 stok eklendi')
      } catch (e) {
        fail(e)
      }
      refreshIfOffline()
    },
    [toast, fail, refreshIfOffline],
  )

  const confirm = useCallback(
    async (id: string) => {
      try {
        await api.confirm(id)
        toast('success', 'Sipariş onaylandı')
      } catch (e) {
        fail(e)
      }
      refreshIfOffline()
    },
    [toast, fail, refreshIfOffline],
  )

  const cancel = useCallback(
    async (id: string) => {
      try {
        await api.cancel(id)
        toast('info', 'Rezervasyon iptal edildi, stok iade edildi')
      } catch (e) {
        fail(e)
      }
      refreshIfOffline()
    },
    [toast, fail, refreshIfOffline],
  )

  const runRace = useCallback(
    async (barcode: string, count: number) => {
      setRaceRunning(true)
      try {
        setReport(await api.race(barcode, count))
      } catch (e) {
        fail(e)
      } finally {
        setRaceRunning(false)
      }
      refreshIfOffline()
    },
    [fail, refreshIfOffline],
  )

  const effectiveRaceBarcode = products.some((p) => p.barcode === raceBarcode) ? raceBarcode : (products[0]?.barcode ?? '')
  const ui = STATUS_UI[status]

  return (
    <div className="space-y-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold">Stok Paneli</h1>
          <p className="text-sm text-slate-500 dark:text-slate-400">Atomik stok rezervasyonu · canlı akış</p>
        </div>
        <span className="flex items-center gap-2 rounded-full border border-slate-300 px-3 py-1.5 text-sm dark:border-slate-700">
          <span className={`h-2.5 w-2.5 rounded-full ${ui.dot}`} />
          {ui.text}
        </span>
      </header>

      {loadError && (
        <div className="rounded-lg border border-rose-500/40 bg-rose-500/10 p-3 text-sm text-rose-600 dark:text-rose-300">
          {loadError}{' '}
          <button className="underline" onClick={() => void loadAll()}>
            Tekrar dene
          </button>
        </div>
      )}

      {canReserve && (
        <BarcodeSimulator value={scan} onChange={setScan} onSubmit={reserve} products={products} sessionId={sessionId} />
      )}

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          <div className="grid gap-4 sm:grid-cols-2">
            {products.map((p) => (
              <ProductCard
                key={p.id}
                product={p}
                selected={scan === p.barcode}
                onSelect={setScan}
                onReserve={reserve}
                onRestock={restock}
                canReserve={canReserve}
                canRestock={canRestock}
              />
            ))}
          </div>
          {canSimulate && (
            <RaceTest
              products={products}
              barcode={effectiveRaceBarcode}
              onBarcodeChange={setRaceBarcode}
              running={raceRunning}
              report={report}
              onRun={runRace}
            />
          )}
        </div>

        <div className="space-y-4">
          <ReservationList reservations={reservations} onConfirm={confirm} onCancel={cancel} canManage={canReserve} />
          <LogFeed logs={logs} products={products} />
        </div>
      </div>
    </div>
  )
}
