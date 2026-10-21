# API Uç Noktaları

Base URL: `http://localhost:5080` · Swagger UI: `http://localhost:5080/swagger`
İçerik tipi: `application/json` (UTF-8) · Tarihler **UTC** ISO-8601 (`2026-10-20T11:29:08Z`)

**Kimlik doğrulama:** `Authorization: Bearer <token>` (`/api/auth/login` veya `/register` yanıtındaki `token`).
Erişim sütunu: 🌐 herkese açık · 🔑 giriş yapmış kullanıcı · 🛡️ yetki gerektirir (yetki adı yanında; `ADMIN` hepsine sahiptir).

> Frontend geliştirme sunucusu (`http://localhost:5173`) `/api` ve `/hubs` isteklerini otomatik olarak bu adrese yönlendirir.

## Hata formatı

```json
{ "success": false, "code": "INSUFFICIENT_STOCK", "message": "Yetersiz stok.", "availableStock": 0 }
```

| HTTP | `code` | Anlamı |
|------|--------|--------|
| 400 | `INVALID_REQUEST` | Eksik/hatalı alan |
| 400 | `EMPTY_CART` | Sepet boş veya süresi dolmuş (ödeme) |
| 401 | `INVALID_CREDENTIALS` / (gövdesiz) | Hatalı giriş / token yok veya geçersiz |
| 402 | `PAYMENT_DECLINED` | Ödeme reddedildi |
| 403 | — | Rol yetersiz (örn. müşteri → admin ucu) |
| 404 | `PRODUCT_NOT_FOUND`, `RESERVATION_NOT_FOUND`, `CARD_NOT_FOUND` | Kayıt yok (başkasına ait kayıtlar da 404 döner) |
| 409 | `INSUFFICIENT_STOCK` | Stok yetersiz (`availableStock` kalan stoğu verir) |
| 409 | `RESERVATION_NOT_ACTIVE`, `EMAIL_TAKEN`, `CART_CHANGED` | Durum çakışması |
| 410 | `RESERVATION_EXPIRED`, `CART_EXPIRED` | Süre dolmuş (stok iade edilir, ödeme alınmaz/iptal edilir) |
| 423 | `ACCOUNT_LOCKED` | 5 hatalı girişten sonra 5 dk kilit |
| 429 | — | Çok fazla istek (kimlik uçlarında IP başına 20/dk) |

---

## Kimlik doğrulama

### `POST /api/auth/register` 🌐
```json
{ "email": "ayse@example.com", "password": "Parola123", "fullName": "Ayşe Yılmaz", "phone": "+90 532 000 11 22" }
```
Parola: ≥ 8 karakter, en az bir harf ve bir rakam. `phone` isteğe bağlıdır.
`201 Created`: `{ "token": "...", "expiresAt": "...", "user": { "id": "...", "email": "...", "fullName": "...", "role": "CUSTOMER" } }`
Hatalar: `400`, `409 EMAIL_TAKEN`.

### `POST /api/auth/login` 🌐
**Tek giriş uç noktası** (müşteri, personel ve yönetici aynı hesapla girer). `user.role` kişinin en yüksek rolünü verir
(`CUSTOMER`, `STAFF`, `ADMIN`); personel/yönetici de alışveriş uçlarını kullanabilir.
```json
{ "email": "ayse@example.com", "password": "Parola123" }
```
`200 OK`: register ile aynı yanıt; yetkili kullanıcılar için `user.permissions` dolu gelir. Yetkili oturumları 2 saat, diğerleri 8 saat geçerlidir.
Hatalar: `401 INVALID_CREDENTIALS`, `403 ACCOUNT_DISABLED`, `423 ACCOUNT_LOCKED`.
Kayıt olan herkes müşteri olur; personel/yönetici yetkisini yalnızca ADMIN verir (`PUT /api/admin/users/{id}`).

### `GET /api/auth/me` 🔑
`{ "id", "email", "fullName", "role": "CUSTOMER|STAFF|ADMIN", "permissions": ["stock.view", ...] }`
(ADMIN için tüm yetkiler listelenir; MÜŞTERİ için boş dizi.) Arayüz yetkiye göre menü/buton gösterimi için kullanır.

---

## Ürünler

### `GET /api/products` 🌐
```json
[{ "id": 1, "name": "Kablosuz Kulaklık Pro (Flash Sale)", "sku": "SKU-HP-001", "barcode": "8690000000011",
   "price": 1499.90, "physicalStock": 5, "reservedStock": 0, "availableStock": 5,
   "version": 1, "updatedAt": "2026-10-20T11:21:13Z" }]
```

### `GET /api/products/barcode/{barcode}` 🌐
Tek ürün (yukarıdaki nesne). Bulunamazsa `404 PRODUCT_NOT_FOUND`.

---

## Sepet ve sipariş (giriş yapmış herkes) 🔑

> Müşteri, personel ve yönetici aynı sepet/sipariş uçlarını kendi hesabıyla kullanır.

> Sepet = kullanıcının `ACTIVE` rezervasyonlarıdır. Sepete eklenen ürün 5 dakika boyunca atomik olarak ayrılır.
> Kullanıcı kimliği her zaman token'dan alınır.

### `GET /api/cart`
```json
{
  "lines": [{ "reservationId": "...", "productId": 2, "productName": "Akıllı Saat Series 7", "barcode": "8690000000028",
              "sku": "SKU-SW-002", "unitPrice": 3299.00, "quantity": 2, "lineTotal": 6598.00,
              "expiresAt": "2026-10-20T11:58:16Z", "remainingSeconds": 288 }],
  "total": 6598.00, "itemCount": 2
}
```

### `POST /api/cart/items`
```json
{ "barcode": "8690000000028", "quantity": 2 }
```
`quantity`: 1–10. `200 OK`: güncel sepet. Hatalar: `404 PRODUCT_NOT_FOUND`, `409 INSUFFICIENT_STOCK`.

### `DELETE /api/cart/items/{reservationId}`
Ürünü sepetten kaldırır, stoğu iade eder. `200 OK`: güncel sepet. Başkasının satırı → `404`.

### `POST /api/checkout`
Sepetteki **tüm** ürünler için ödemeyi alır ve siparişi oluşturur.

Yeni kartla:
```json
{
  "address": { "fullName": "Ayşe Yılmaz", "phone": "+90 532 000 11 22", "city": "Ankara",
               "district": "Çankaya", "addressLine": "Kızılay Mah. Atatürk Blv. No:10", "postalCode": "06420" },
  "saveAddress": true,
  "card": { "number": "4242 4242 4242 4242", "holderName": "AYSE YILMAZ", "expMonth": 12, "expYear": 2030, "cvv": "123" },
  "saveCard": true
}
```
Kayıtlı kartla (CVV yine gerekir):
```json
{ "address": { "...": "..." }, "savedCardId": "d450582f-...", "cvv": "123" }
```
`201 Created`:
```json
{
  "id": "...", "orderNo": "FS-261020-BE4H63", "totalPrice": 14597.00, "status": "PAID",
  "paymentBrand": "Visa", "paymentLast4": "4242",
  "shippingAddress": { "fullName": "...", "phone": "...", "city": "...", "district": "...", "addressLine": "...", "postalCode": "..." },
  "createdAt": "2026-10-20T11:53:57Z",
  "items": [{ "productId": 2, "productName": "Akıllı Saat Series 7", "barcode": "8690000000028", "quantity": 2, "lineTotal": 6598.00 }]
}
```
Hatalar: `400` (doğrulama/boş sepet), `402 PAYMENT_DECLINED`, `410 CART_EXPIRED` (ödeme alınmaz), `409 CART_CHANGED`.
CVV ve tam kart numarası hiçbir yerde saklanmaz/loglanmaz; kayıtlı kartın numarası şifrelidir (AES-256-GCM).

### `GET /api/my/orders`
Kullanıcının son 50 siparişi (yukarıdaki sipariş nesnesi; adres sunucuda çözülerek döner).

### `GET /api/my/cards`
`[{ "id", "brand": "Visa", "last4": "4242", "holderName": "AYSE YILMAZ", "expMonth": 12, "expYear": 2030 }]` (kart numarası asla dönmez)

### `DELETE /api/my/cards/{cardId}`
`204 No Content` / `404 CARD_NOT_FOUND`.

### `GET /api/my/profile` · `PUT /api/my/profile`
```json
{ "email": "ayse@example.com", "fullName": "Ayşe Yılmaz", "phone": "+90 532 000 11 22", "role": "CUSTOMER",
  "address": { "fullName": "...", "phone": "...", "city": "...", "district": "...", "addressLine": "...", "postalCode": "..." } }
```
`PUT` gövdesi: `{ "fullName", "phone", "address" }` (`address: null` adresi siler). Telefon ve adres şifreli saklanır.

---

## Stok işlemleri

### `POST /api/inventory/reserve` 🛡️ `reservations.manage`
El terminali (personel): stoğu **atomik** olarak düşüp rezerve eder (5 dk geçerli). `userId` verilmezse `STAFF-{personelId}` kullanılır.
Müşteriler bu ucu değil, sepeti (`/api/cart/items`) kullanır.
```json
{ "barcode": "8690000000011", "quantity": 1, "userId": "WEB-ABC123" }
```
`200 OK`:
```json
{ "reservationId": "...", "barcode": "8690000000011", "productName": "...", "quantity": 1,
  "status": "ACTIVE", "expiresAt": "2026-10-20T11:34:08Z", "remainingSeconds": 300, "availableStock": 4 }
```
Hatalar: `400`, `404`, `409 INSUFFICIENT_STOCK`.

### `POST /api/inventory/cancel` 🛡️ `reservations.manage`
`{ "reservationId": "..." }` — herhangi bir `ACTIVE` rezervasyonu iptal eder, stoğu iade eder. Müşteriler kendi sepetlerinden `DELETE /api/cart/items/{id}` ile kaldırır.
`200 OK`: `{ "reservation": {...}, "product": {...} }`

### `POST /api/inventory/restock` 🛡️ `stock.restock`
`{ "barcode": "8690000000011", "addQuantity": 10 }` → `{ "product": {...}, "addedQuantity": 10 }`

### `POST /api/orders/confirm` 🛡️ `reservations.manage`
Yönetici/el terminali: tek bir `ACTIVE` rezervasyonu doğrudan siparişe çevirir (müşteriler `/api/checkout` kullanır).
`{ "reservationId": "..." }` → `{ "order": {...}, "reservation": {...}, "product": {...} }`. Süre dolmuşsa `410`.

---

## Yönetim 🛡️

### `GET /api/reservations?limit=100` 🛡️ `stock.view`
Tüm kullanıcıların son rezervasyonları (en fazla 500).
```json
[{ "id": "...", "productId": 1, "productName": "...", "barcode": "...", "userId": "...", "quantity": 1,
   "status": "ACTIVE", "expiresAt": "...", "createdAt": "...", "remainingSeconds": 287 }]
```

### `GET /api/logs?limit=50` 🛡️ `stock.view`
`stock_movement_logs` kayıtları. `actionType`: `RESERVE`, `CONFIRM_ORDER`, `EXPIRE_ROLLBACK`, `CANCEL`, `MANUAL_RESTOCK`.

### `POST /api/simulation/race-condition` 🛡️ `simulation.run`
Backend içinden `Task.WhenAll` ile aynı anda paralel rezervasyon isteği ateşler.
```json
{ "barcode": "8690000000011", "requestCount": 100, "quantity": 1 }
```
`requestCount` varsayılan 100 (en fazla 500). Yanıt:
```json
{ "barcode": "...", "productName": "...", "requestCount": 100, "quantityPerRequest": 1,
  "successCount": 5, "failedCount": 95, "errorCount": 0,
  "initialAvailableStock": 5, "finalAvailableStock": 0, "finalReservedStock": 5,
  "oversellingPrevented": true, "stockConsistent": true,
  "totalElapsedMs": 47.01, "minMs": 22.61, "avgMs": 34.0, "p95Ms": 38.12, "maxMs": 41.16,
  "results": [{ "index": 1, "userId": "SIM-a1b2c3-001", "success": false, "status": "INSUFFICIENT_STOCK", "durationMs": 31.2, "reservationId": null }] }
```
Başarılı simülasyon rezervasyonları gerçektir; 5 dk içinde iade edilir.

### `GET /api/health` 🌐
`{ "status": "ok", "timeUtc": "..." }`

---

## Hesap ve yetki yönetimi

Herkesin **tek hesabı** vardır (`users`: tek e-posta, tek parola) ve herkes müşteridir. Personel/yönetici yetkileri ayrı **`staff_users`** tablosunda
(`user_id` ile kişiye bağlı) tutulur; yetki kaydı olan kişi tek girişle hem müşteri hem personel/yönetici olarak çalışır.
Yetki anahtarları: `stock.view`, `stock.restock`, `reservations.manage`, `simulation.run`, `orders.view` (personele verilebilir) ve
`users.manage` (**devredilemez**, yalnızca `ADMIN`). `ADMIN` hepsine sahiptir. Rol ve yetkiler **her istekte veritabanından** okunur;
değişiklikler mevcut token'lar için de anında geçerlidir, devre dışı bırakılan hesabın token'ı `401` döner.
Yetkisi olmayan çağrı `403` alır.

### `GET /api/admin/permissions` 🛡️ `ADMIN`
`[{ "key": "stock.view", "label": "Stok panelini görüntüleme", "description": "..." }, ...]` (devredilebilir yetkiler kataloğu)

### `GET /api/admin/users` 🛡️ `ADMIN`
Tüm kullanıcılar (en fazla 1000; yöneticiler, personel, sonra yalnızca müşteriler). Telefon/adres dönmez.
```json
[{ "id": "...", "email": "depo@shop.local", "fullName": "Depo Görevlisi", "role": "STAFF",
   "permissions": ["stock.restock", "stock.view"], "isActive": true,
   "createdAt": "2026-10-20T12:18:49Z", "lastLoginAt": null }]
```
`role`: yetki kaydı yoksa `CUSTOMER`, varsa `STAFF` veya `ADMIN`.

### `POST /api/admin/users` 🛡️ `ADMIN`
```json
{ "email": "depo@shop.local", "password": "Depo12345", "fullName": "Depo Görevlisi",
  "role": "STAFF", "permissions": ["stock.view", "stock.restock"] }
```
`role`: `CUSTOMER` | `STAFF` | `ADMIN`. `permissions` yalnızca `STAFF` için anlamlıdır (ADMIN zaten hepsine sahip, CUSTOMER hiçbirine).
`users.manage` veya bilinmeyen bir yetki verilirse `400`. `201 Created`: kullanıcı nesnesi. Hatalar: `400`, `409 EMAIL_TAKEN`
(e-posta zaten kayıtlıysa yetki vermek için mevcut hesabı `PUT` ile güncelleyin).

### `PUT /api/admin/users/{id}` 🛡️ `ADMIN`
Verilen alanlar güncellenir: `{ "fullName"?, "role"?, "permissions"?, "isActive"? }`.
`role: "STAFF" | "ADMIN"` yetki kaydını ekler/günceller, `role: "CUSTOMER"` yetki kaydını siler (kişi müşteri olarak kalır).
`isActive: false` hesabı tamamen devre dışı bırakır (giriş `403`, mevcut token `401`).
Hatalar: `409 SELF_LOCKOUT` (kendi rolünüz/aktifliğiniz), `409 LAST_ADMIN` (son aktif yönetici), `404`, `400`.

### `POST /api/admin/users/{id}/reset-password` 🛡️ `ADMIN`
`{ "newPassword": "Yeni12345" }` → `204 No Content`. Hesap kilidini de kaldırır.

### `GET /api/admin/orders?limit=100` 🛡️ `orders.view`
Tüm müşteri siparişleri. Tam adres/telefon dönmez; yalnızca il/ilçe.
```json
[{ "id": "...", "orderNo": "FS-261020-BE4H63", "customerEmail": "...", "customerName": "...", "totalPrice": 4798.90,
   "status": "PAID", "paymentBrand": "Visa", "paymentLast4": "4242", "city": "Ankara", "district": "Çankaya",
   "createdAt": "...", "items": [{ "productId": 2, "productName": "...", "barcode": "...", "quantity": 1, "lineTotal": 3299.00 }] }]
```

---

## Gerçek zamanlı (SignalR)

Hub: `/hubs/stock` (WebSocket). `stock.view` yetkisi olan (veya ADMIN) kullanıcının token'ı `access_token` ile verilirse (`accessTokenFactory`) bağlantı **yönetim grubuna** alınır.

| Olay | Alıcı | Yük |
|------|-------|-----|
| `ProductUpdated` | Herkes | Ürün nesnesi (`version` her değişimde artar; eski olayı bu alanla eleyin) |
| `ReservationUpdated` | `stock.view` yetkililer | Rezervasyon nesnesi |
| `StockLogCreated` | `stock.view` yetkililer | Log nesnesi |

```ts
const hub = new HubConnectionBuilder()
  .withUrl('http://localhost:5080/hubs/stock', { accessTokenFactory: () => token ?? '' })
  .withAutomaticReconnect().build()
hub.on('ProductUpdated', (p) => console.log(p.barcode, p.availableStock))
await hub.start()
```
