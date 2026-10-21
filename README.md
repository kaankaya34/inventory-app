# FlashShop – Yüksek Trafikli Stok & Sipariş Rezervasyon Motoru

Flash Sale anlarında eşzamanlı gelen rezervasyon isteklerini **race condition ve overselling olmadan** işleyen stok motoru
üzerine kurulu bir **satış sitesi**: üyelik/giriş, sepet, kart ile ödeme, siparişlerim ve yönetim paneli.

Stack: **MySQL/MariaDB** + **.NET 8 Web API** (Dapper + MySqlConnector, JWT, SignalR, BackgroundService) + **React (Vite + TailwindCSS)**.

```
inventory-app/
├── veritabani.md             # phpMyAdmin'de çalıştırılacak SQL (kurulum / yükseltme / sıfırlama)
├── apis.md                   # Tüm API uç noktaları ve SignalR olayları
├── backend/StockEngine.Api   # .NET 8 Web API
└── frontend/                 # React + Vite + Tailwind (mağaza + /admin paneli)
```

## Gereksinimler
* MySQL 8 / MariaDB 10.4+ (XAMPP uygundur) ve phpMyAdmin
* .NET 8 SDK/runtime, Node.js 20+

## Kurulum ve çalıştırma

### 1) Veritabanı
MySQL'i başlatın, phpMyAdmin → **SQL** sekmesinde [veritabani.md](./veritabani.md) içinden **doğru bloğu** çalıştırın:
* İlk kurulum → **1) Sıfırdan kurulum**
* Yalnızca Faz 1 (stok motoru) kuruluysa → **2) Faz 1'den yükseltme**
* Önceki sürümde ayrı e-postalı/parolalı `staff_users` tablosu varsa → **3) Personel hesaplarını kullanıcılara bağlama**
* Önceki sürümde `users` tablosunda `role` kolonu varsa → **4) `users.role` kolonundan taşıma**

### 2) Backend (http://localhost:5080)
```powershell
cd backend\StockEngine.Api
dotnet run
```
İlk çalıştırmada otomatik olur:
* `App_Data\encryption.key` (alan şifreleme anahtarı) ve `App_Data\jwt.key` (token imza anahtarı) üretilir.
* Hiç yönetici yoksa **admin@shop.local** hesabı oluşturulur; rastgele parolası **`App_Data\admin-credentials.txt`** dosyasına yazılır.
  (Kendi parolanızı vermek için: `Admin__Password` ortam değişkeni, e-posta için `Admin__Email`.)
* Konsolda `MySQL bağlantısı başarılı. 5 ürün bulundu.` görmelisiniz. Swagger: http://localhost:5080/swagger

> ⚠ `App_Data` klasörünü kaybederseniz şifrelenmiş veriler (kayıtlı kartlar, adresler, telefonlar) **bir daha çözülemez**. Yedekleyin; git'e eklemeyin (`.gitignore`'da).

### 3) Frontend (http://localhost:5173)
```powershell
cd frontend
npm install
npm run dev
```
Vite, `/api` ve `/hubs` isteklerini backend'e yönlendirir. Farklı backend adresi: `VITE_BACKEND_URL=http://host:port npm run dev`.

## Sayfalar

| Adres | Açıklama | Erişim |
|-------|----------|--------|
| `/` | Mağaza: ürünler, canlı stok rozeti (Stokta / Son N adet / Tükendi), sepete ekle | Herkes (sepete eklemek için giriş) |
| `/giris`, `/kayit` | Giriş (müşteri, personel ve yönetici **aynı hesapla, aynı sayfadan**) / kayıt (kayıt olan herkes müşteri olur) | Herkes |
| `/sepet` | Sepet, ürün başına geri sayım (5 dk ayrılma süresi) | Giriş yapmış |
| `/odeme` | Teslimat adresi + kart bilgisi (yeni kart veya kayıtlı kart) | Giriş yapmış |
| `/siparislerim` | Geçmiş siparişler (adres/kart özeti çözülerek gösterilir) | Giriş yapmış |
| `/hesabim` | Profil, varsayılan adres, kayıtlı kartları silme | Giriş yapmış |
| `/admin` | Stok paneli: canlı stok, barkod simülatörü, rezervasyonlar, 🔥 race condition testi, audit log (butonlar yetkiye göre görünür) | `stock.view` |
| `/admin/siparisler` | Tüm müşteri siparişleri (adres/telefon gösterilmez, yalnızca il/ilçe) | `orders.view` |
| `/admin/kullanicilar` | **Hesaplar ve Yetkiler:** hesap oluşturma, rol/yetki verme veya kaldırma, devre dışı bırakma, parola sıfırlama | Yalnızca ADMIN |

## Hesaplar: tek hesap, ayrı yetki tablosu

Bir kişi aynı anda **müşteri, personel ve yönetici** olabilir; bu yüzden herkesin **tek hesabı, tek e-postası, tek parolası** vardır:

| Tablo | Ne tutar |
|-------|----------|
| `users` | **Herkes**: e-posta, parola özeti, ad, telefon/adres (şifreli), aktiflik. Kayıt olan herkes buraya düşer ve **müşteri** olur. |
| `staff_users` | **Yalnızca yetki kaydı**: hangi kişi (`user_id` → `users.id`) **personel (STAFF)** veya **yönetici (ADMIN)** ve hangi yetkilere sahip. |

Yetki kaydı olmayan kişi müşteridir. Yetki kaydı olan kişi **tek girişle** hem alışveriş yapar hem yetkileri kadar yönetim yapar.
Yetkiyi kaldırmak `staff_users` kaydını silmektir; kişi yine müşteri olarak kalır. Yetkili oturumları daha kısadır (2 saat, diğerleri 8 saat).

## Roller ve yetkiler

| Rol | Nerede tutulur | Ne yapabilir |
|-----|----------------|--------------|
| **CUSTOMER (Müşteri)** | `users` | Alışveriş: sepet, ödeme, siparişlerim, hesabım. **Herkes** bunu yapabilir. |
| **STAFF (Personel)** | `users` + `staff_users` | Müşteri olarak yapabildikleri **+** size verdiğiniz yetkiler. |
| **ADMIN (Yönetici)** | `users` + `staff_users` | Müşteri olarak yapabildikleri **+ her şey** (tüm yetkiler, hesap yönetimi). Yetkileri kısıtlanamaz. |

Personele verilebilen yetkiler (hesap oluştururken ya da sonradan **Hesaplar ve Yetkiler** sayfasından, hazır şablonlarla birlikte):

| Yetki | Anahtar | Neye izin verir |
|-------|---------|-----------------|
| Stok panelini görüntüleme | `stock.view` | `/admin` paneli, canlı rezervasyon ve log akışı |
| Stok ekleme | `stock.restock` | "+10 Stok Ekle", `POST /api/inventory/restock` |
| Rezervasyon yönetimi | `reservations.manage` | El terminaliyle rezerve etme, başkalarının rezervasyonunu onaylama/iptal etme |
| Yük testi | `simulation.run` | Race condition testi |
| Tüm siparişleri görüntüleme | `orders.view` | `/admin/siparisler` |
| *(devredilemez)* Kullanıcı yönetimi | `users.manage` | Yalnızca ADMIN: hesap oluşturma, rol/yetki değiştirme, devre dışı bırakma, parola sıfırlama |

**Nasıl kullanılır:** ADMIN olarak `/giris` sayfasından girin → **👥 Hesaplar ve Yetkiler**. Listedeki herkes için **🛠️ Yetki ver** butonuyla rolü
*Personel* (yetkileri işaretleyin) veya *Yönetici* yapın; **Kaydet** deyince yetki **anında** geçerli olur, kişinin yeniden giriş yapması gerekmez.
Yetkiyi almak için rolü *Müşteri* yapın. Yeni biri için **➕ Yeni Hesap Oluştur** kullanın. Müşteri kendisi de `/kayit` ile kayıt olabilir.

**Güvenlik dengeleri:**
* Yetkiler her istekte **veritabanından** okunur (JWT'ye güvenilmez): yetki değişikliği veya hesabı devre dışı bırakma, kişi çıkış yapmasa bile **anında** geçerli olur.
* Kullanıcı yönetimi devredilemez; bir personel kendine veya başkasına yetki yükseltemez.
* Kendi rolünüzü/aktifliğinizi değiştiremez, **son aktif yöneticiyi** kaldıramazsınız.
* Arayüzdeki gizleme yalnızca kolaylıktır; her uç nokta sunucuda ayrıca yetki kontrolünden geçer (yetkisiz → `403`).
* Müşteriler `userId` göndererek başkası adına rezervasyon yapamaz (token'daki kimlik kullanılır).
* Yetki değişiklikleri sunucu loguna `YETKİ:` ön ekiyle yazılır.

**Nasıl çalışır:** "Sepete ekle" = stok motorunda **5 dakikalık atomik rezervasyon**. Sepet, kullanıcının `ACTIVE` rezervasyonlarıdır.
Ödemede tüm sepet tek bir veritabanı transaction'ında siparişe dönüşür; süre dolarsa arka plan servisi stoğu otomatik iade eder.

## Test ödeme kartları (gerçek tahsilat yapılmaz)

| Kart | Sonuç |
|------|-------|
| `4242 4242 4242 4242` | Onaylanır (Visa) |
| `5555 5555 5555 4444` | Onaylanır (Mastercard) |
| `4000 0000 0000 0002` | Banka reddeder |
| `4000 0000 0000 9995` | Yetersiz bakiye |

Son kullanma tarihi gelecekte, CVV 3-4 hane olmalıdır; kart numarası Luhn kontrolünden geçmelidir.

## Güvenlik özeti

| Konu | Uygulama |
|------|----------|
| Parola | PBKDF2-SHA256, 210.000 iterasyon, kullanıcıya özel tuz; sabit-zamanlı karşılaştırma |
| Hassas alanlar | **AES-256-GCM** ile uygulama içinde şifrelenir: telefon, adresler, kart numarası, kart sahibi. Anahtar veritabanında değil `App_Data`/ortam değişkeninde. Her kolon için ayrı "purpose" (AAD) bağlanır |
| Kart verisi | **CVV hiçbir yerde saklanmaz/loglanmaz.** Kayıtlı kartla ödemede her seferinde yeniden istenir. Kayıtlı kart API'den asla numara döndürmez (marka + son 4 hane) |
| Kimlik doğrulama | JWT (HS256): yetkili oturumları 2 saat, diğerleri 8 saat. Token yalnızca kimliği taşır; rol ve yetkiler her istekte `users` + `staff_users` tablolarından okunur |
| Yetkilendirme | Kullanıcı kimliği **her zaman token'dan** alınır; başkasının sepeti/siparişi/kartı erişilemez (404). Yönetim uçları yetki bazlıdır (yukarıdaki tablo), ADMIN hepsine sahiptir |
| Kaba kuvvet | 5 hatalı girişte hesap 5 dk kilitlenir; kimlik uçlarında IP başına dakikada 20 istek sınırı; kullanıcı yokken de parola doğrulama süresi harcanır |
| SignalR | Stok yayını herkese; rezervasyon/log olayları yalnızca `stock.view` yetkisi olanlara (ve ADMIN'e) |
| Başlıklar | `X-Content-Type-Options`, `X-Frame-Options`, `/api` için `Cache-Control: no-store` |

**Canlıya almadan önce yapılması gerekenler** (şu an kapsam dışı): gerçek ödeme sağlayıcısı (iyzico/Stripe; kart verisi sunucuya hiç uğramadan token ile),
HTTPS + HSTS, anahtarların ortam değişkeni/secret manager ile verilmesi (`Encryption__Key`, `Jwt__Key` – base64), JWT'yi `HttpOnly` çerezde tutmak (şu an localStorage),
e-posta doğrulama / parola sıfırlama, KVKK aydınlatma metni, yedekleme ve anahtar rotasyonu.

## Mimari notlar
* **Atomik rezervasyon (MySQL native):** stok kontrolü ve düşümü tek ifadede:
  `UPDATE products SET available_stock = available_stock - @qty, reserved_stock = reserved_stock + @qty WHERE barcode = @barcode AND available_stock >= @qty`.
  InnoDB satır kilidi istekleri sıraya dizer; etkilenen satır `0` ise `409 Yetersiz Stok`. `CHECK` kısıtları stoğun eksiye düşmesini veritabanı seviyesinde de engeller.
* **Redis:** kurulu olmadığı için uygulanmadı; stok mantığı `IInventoryService` arkasında, Redis/Lua implementasyonu aynı arayüzle takılabilir.
* **Kilit sırası + deadlock retry:** onay/iptal/ödeme/süre dolumu akışlarında önce `reservations`, sonra `products` satırı kilitlenir.
* **Ödeme akışı:** (1) sepet doğrulanır → (2) kart tahsil edilir → (3) tek transaction'da tüm rezervasyonlar siparişe çevrilir.
  3. adım başarısız olursa (örn. sepet süresi tam o anda doldu) ödeme **iptal (void)** edilir ve stok iade edilir.
* **Süre dolumu:** `ReservationExpiryWorker` her saniye süresi dolan rezervasyonları iade eder (`Reservation:TtlMinutes`, `Reservation:CleanupIntervalSeconds`).
* **Barkod altyapısı:** `/api/products/barcode/{barcode}`, `reserve`, `restock` barkod bazlıdır; mobil faz bu uçları kullanacaktır.
* **Zaman:** tüm tarihler UTC saklanır ve UTC döner.

## Sorun giderme
* *"Veritabanı hazır değil" hatası:* `veritabani.md`'deki doğru bloğu çalıştırdınız mı? Yeni tablolar (`users` vb.) yoksa **2) Yükseltme** bloğunu çalıştırın.
* *Yönetici parolasını unuttum:* `App_Data\admin-credentials.txt` dosyasına bakın; silinmişse `users` tablosundan admin satırını silip backend'i yeniden başlatın (yeni parola üretilir).
* *Şifre çözme hatası / adres görünmüyor:* `App_Data\encryption.key` değişti veya silindi; eski şifreli veriler çözülemez.
* *Dashboard "Bağlantı yok":* Backend'in `http://localhost:5080` adresinde çalıştığından emin olun.
