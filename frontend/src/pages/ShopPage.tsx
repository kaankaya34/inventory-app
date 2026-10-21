import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../lib/api'
import { useAuth } from '../lib/auth'
import { useCart } from '../lib/cart'
import { useToast } from '../lib/toast'
import type { Product } from '../lib/types'
import { useStockHub } from '../lib/useStockHub'
import { btn, card, money, productEmoji } from '../lib/ui'

function StockBadge({ available }: { available: number }) {
  if (available <= 0)
    return <span className="rounded-full bg-rose-500/15 px-2 py-0.5 text-xs font-medium text-rose-500">Tükendi</span>
  if (available <= 5)
    return (
      <span className="rounded-full bg-amber-500/15 px-2 py-0.5 text-xs font-medium text-amber-600 dark:text-amber-400">
        Son {available} adet!
      </span>
    )
  return <span className="rounded-full bg-emerald-500/15 px-2 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400">Stokta</span>
}

function ProductTile({ product: p }: { product: Product }) {
  const { user } = useAuth()
  const { add } = useCart()
  const toast = useToast()
  const navigate = useNavigate()
  const [qty, setQty] = useState(1)
  const [busy, setBusy] = useState(false)

  const max = Math.max(1, Math.min(10, p.availableStock))
  const q = Math.min(qty, max)

  const addToCart = async () => {
    if (!user) {
      navigate('/giris', { state: { from: '/' } })
      return
    }
    setBusy(true)
    try {
      await add(p.barcode, q)
      toast('success', `${q} × ${p.name} sepete eklendi (5 dk ayrıldı)`)
    } catch (e) {
      toast('error', e instanceof Error ? e.message : 'Sepete eklenemedi')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={`${card} flex flex-col`}>
      <div className="flex h-32 items-center justify-center rounded-lg bg-gradient-to-br from-indigo-500/10 to-fuchsia-500/10 text-6xl">
        {productEmoji(p.sku)}
      </div>
      <div className="mt-3 flex items-start justify-between gap-2">
        <h3 className="font-semibold leading-tight">{p.name}</h3>
        <StockBadge available={p.availableStock} />
      </div>
      <div className="mt-1 text-xl font-bold text-indigo-600 dark:text-indigo-400">{money(p.price)}</div>

      <div className="mt-auto flex items-center gap-2 pt-3">
        <select
          className="rounded-lg border border-slate-300 bg-white px-2 py-2 text-sm dark:border-slate-700 dark:bg-slate-950"
          value={q}
          disabled={p.availableStock <= 0}
          onChange={(e) => setQty(Number(e.target.value))}
          aria-label="Adet"
        >
          {Array.from({ length: max }, (_, i) => i + 1).map((n) => (
            <option key={n} value={n}>{n}</option>
          ))}
        </select>
        <button
          className={`${btn} flex-1 bg-indigo-600 text-white hover:bg-indigo-500`}
          disabled={busy || p.availableStock <= 0}
          onClick={addToCart}
        >
          {p.availableStock <= 0 ? 'Tükendi' : busy ? 'Ekleniyor…' : '🛒 Sepete Ekle'}
        </button>
      </div>
    </div>
  )
}

export default function ShopPage() {
  const [products, setProducts] = useState<Product[]>([])
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setProducts(await api.products())
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ürünler yüklenemedi')
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  // Stok değiştikçe (başka biri satın aldığında) kartlar sayfa yenilenmeden güncellenir.
  useStockHub({
    onProduct: (p) =>
      setProducts((prev) => {
        const ex = prev.find((x) => x.id === p.id)
        if (ex && ex.version > p.version) return prev
        return (ex ? prev.map((x) => (x.id === p.id ? p : x)) : [...prev, p]).sort((a, b) => a.id - b.id)
      }),
    onConnected: () => void load(),
  })

  return (
    <div className="space-y-5">
      <section className="rounded-2xl bg-gradient-to-r from-indigo-600 via-violet-600 to-fuchsia-600 p-6 text-white md:p-8">
        <h1 className="text-2xl font-extrabold md:text-3xl">🔥 Flash Sale başladı!</h1>
        <p className="mt-1 max-w-xl text-sm text-indigo-100 md:text-base">
          Stoklar sınırlı ve anlık güncelleniyor. Sepete eklediğiniz ürünler <b>5 dakika</b> boyunca sizin için ayrılır.
        </p>
      </section>

      {error && (
        <div className="rounded-lg border border-rose-500/40 bg-rose-500/10 p-3 text-sm text-rose-600 dark:text-rose-300">
          ⚠ {error}{' '}
          <button className="underline" onClick={() => void load()}>Tekrar dene</button>
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {products.map((p) => (
          <ProductTile key={p.id} product={p} />
        ))}
      </div>
    </div>
  )
}
