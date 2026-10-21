import type { ChangeEvent } from 'react'
import type { Address } from '../lib/types'
import { input, label } from '../lib/ui'

export const emptyAddress: Address = { fullName: '', phone: '', city: '', district: '', addressLine: '', postalCode: '' }

export function AddressForm({
  value,
  onChange,
  required = true,
}: {
  value: Address
  onChange: (a: Address) => void
  required?: boolean
}) {
  const set = (k: keyof Address) => (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    onChange({ ...value, [k]: e.target.value })

  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <div>
        <label className={label}>Ad Soyad</label>
        <input className={`${input} w-full`} required={required} autoComplete="name" value={value.fullName} onChange={set('fullName')} />
      </div>
      <div>
        <label className={label}>Telefon</label>
        <input className={`${input} w-full`} required={required} type="tel" autoComplete="tel" placeholder="+90 5xx xxx xx xx" value={value.phone} onChange={set('phone')} />
      </div>
      <div>
        <label className={label}>İl</label>
        <input className={`${input} w-full`} required={required} autoComplete="address-level1" value={value.city} onChange={set('city')} />
      </div>
      <div>
        <label className={label}>İlçe</label>
        <input className={`${input} w-full`} required={required} autoComplete="address-level2" value={value.district} onChange={set('district')} />
      </div>
      <div className="sm:col-span-2">
        <label className={label}>Açık Adres</label>
        <textarea className={`${input} w-full`} required={required} rows={2} autoComplete="street-address" value={value.addressLine} onChange={set('addressLine')} />
      </div>
      <div>
        <label className={label}>Posta Kodu</label>
        <input className={`${input} w-full`} inputMode="numeric" autoComplete="postal-code" value={value.postalCode} onChange={set('postalCode')} />
      </div>
    </div>
  )
}
