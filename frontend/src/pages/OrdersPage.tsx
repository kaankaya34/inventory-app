import { useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { api } from '../lib/api'
import type { CustomerOrder } from '../lib/types'
import { btn, card, dateTimeOf, money } from '../lib/ui'

export default function OrdersPage() {
  const justOrdered = (useLocation().state as { orderNo?: string } | null)?.orderNo
  const [orders, setOrders] = useState<CustomerOrder[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.myOrders().then(setOrders).catch((e) => setError(e instanceof Error ? e.message : 'Siparişler yüklenemedi'))
  }, [])

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <h1 className="text-xl font-bold">Siparişlerim</h1>

      {justOrdered && (
        <div className="rounded-xl border border-emerald-500/40 bg-emerald-500/10 p-4 text-emerald-700 dark:text-emerald-300">
          Ödemeniz alındı ve siparişiniz oluşturuldu. Sipariş numaranız: <b className="font-mono">{justOrdered}</b>
        </div>
      )}
      {error && <p className="text-sm text-rose-500">{error}</p>}

      {orders?.length === 0 && (
        <div className={`${card} py-10 text-center`}>
          <p className="text-slate-500 dark:text-slate-400">Henüz siparişiniz yok.</p>
          <Link to="/" className={`${btn} mt-3 inline-block bg-indigo-600 text-white hover:bg-indigo-500`}>Alışverişe Başla</Link>
        </div>
      )}

      {orders?.map((o) => (
        <article key={o.id} className={`${card} space-y-3 ${o.orderNo === justOrdered ? 'ring-2 ring-emerald-500' : ''}`}>
          <header className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <div className="font-mono font-semibold">{o.orderNo}</div>
              <div className="text-xs text-slate-500 dark:text-slate-400">{dateTimeOf(o.createdAt)}</div>
            </div>
            <span className="rounded-full bg-emerald-500/15 px-2.5 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400">
              {o.status === 'PAID' ? 'Ödendi' : 'İade edildi'}
            </span>
          </header>

          <ul className="divide-y divide-slate-200 text-sm dark:divide-slate-800">
            {o.items.map((i) => (
              <li key={`${o.id}-${i.productId}-${i.quantity}-${i.lineTotal}`} className="flex justify-between py-1.5">
                <span>{i.quantity} × {i.productName}</span>
                <span>{money(i.lineTotal)}</span>
              </li>
            ))}
          </ul>

          <footer className="grid gap-2 border-t border-slate-200 pt-3 text-sm dark:border-slate-800 sm:grid-cols-2">
            <div>
              <div className="text-xs text-slate-500 dark:text-slate-400">Teslimat</div>
              {o.shippingAddress ? (
                <address className="not-italic">
                  {o.shippingAddress.fullName}<br />
                  {o.shippingAddress.addressLine}<br />
                  {o.shippingAddress.district} / {o.shippingAddress.city} {o.shippingAddress.postalCode}
                </address>
              ) : '—'}
            </div>
            <div className="sm:text-right">
              <div className="text-xs text-slate-500 dark:text-slate-400">Ödeme: {o.paymentBrand} •••• {o.paymentLast4}</div>
              <div className="text-lg font-bold">{money(o.totalPrice)}</div>
            </div>
          </footer>
        </article>
      ))}
    </div>
  )
}
