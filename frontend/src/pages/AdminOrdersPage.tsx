import { useEffect, useState } from 'react'
import { api } from '../lib/api'
import { useToast } from '../lib/toast'
import type { AdminOrder } from '../lib/types'
import { card, dateTimeOf, money } from '../lib/ui'

// Yetki: orders.view. Gizlilik gereği tam adres/telefon gösterilmez; yalnızca il/ilçe.
export default function AdminOrdersPage() {
  const toast = useToast()
  const [orders, setOrders] = useState<AdminOrder[] | null>(null)

  useEffect(() => {
    api.adminOrders(200).then(setOrders).catch((e) => toast('error', e instanceof Error ? e.message : 'Siparişler yüklenemedi'))
  }, [toast])

  const revenue = orders?.reduce((s, o) => s + o.totalPrice, 0) ?? 0

  return (
    <div className="space-y-4">
      <header className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h1 className="text-2xl font-bold">📦 Tüm Siparişler</h1>
          <p className="text-sm text-slate-500 dark:text-slate-400">Müşteri adres ve telefon bilgisi bu ekranda gösterilmez.</p>
        </div>
        {orders && (
          <div className="text-right text-sm">
            <div className="text-slate-500 dark:text-slate-400">{orders.length} sipariş</div>
            <div className="text-lg font-bold">{money(revenue)}</div>
          </div>
        )}
      </header>

      <div className={`${card} overflow-x-auto p-0`}>
        <table className="w-full text-left text-sm">
          <thead className="border-b border-slate-200 text-xs uppercase text-slate-500 dark:border-slate-800 dark:text-slate-400">
            <tr>
              <th className="px-4 py-3">Sipariş</th>
              <th className="px-4 py-3">Müşteri</th>
              <th className="px-4 py-3">Ürünler</th>
              <th className="px-4 py-3">Teslimat</th>
              <th className="px-4 py-3">Ödeme</th>
              <th className="px-4 py-3 text-right">Tutar</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
            {orders?.map((o) => (
              <tr key={o.id} className="align-top">
                <td className="px-4 py-3">
                  <div className="font-mono font-medium">{o.orderNo}</div>
                  <div className="text-xs text-slate-500 dark:text-slate-400">{dateTimeOf(o.createdAt)}</div>
                </td>
                <td className="px-4 py-3">
                  <div>{o.customerName}</div>
                  <div className="text-xs text-slate-500 dark:text-slate-400">{o.customerEmail}</div>
                </td>
                <td className="px-4 py-3">
                  {o.items.map((i, idx) => (
                    <div key={idx}>{i.quantity} × {i.productName}</div>
                  ))}
                </td>
                <td className="px-4 py-3">{o.district && o.city ? `${o.district} / ${o.city}` : '—'}</td>
                <td className="px-4 py-3">
                  {o.paymentBrand} •••• {o.paymentLast4}
                  <div className="text-xs text-emerald-600 dark:text-emerald-400">{o.status === 'PAID' ? 'Ödendi' : 'İade'}</div>
                </td>
                <td className="px-4 py-3 text-right font-semibold">{money(o.totalPrice)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {orders?.length === 0 && <p className="p-6 text-center text-slate-500">Henüz sipariş yok.</p>}
      </div>
    </div>
  )
}
