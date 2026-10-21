# Veritabanı Kurulumu (MySQL / MariaDB – phpMyAdmin)

phpMyAdmin → **SQL** sekmesine yapıştırıp **Git** diyerek çalıştırın. Hangi bloğu çalıştıracağınız:

| Durum | Çalıştırılacak blok |
|-------|---------------------|
| Veritabanı hiç kurulmadı | **1) Sıfırdan kurulum** |
| Yalnızca Faz 1 (stok motoru) kurulu; mağaza ve yetki tabloları eklenecek | **2) Faz 1'den yükseltme** |
| Önceki sürümü kurdunuz: **ayrı e-postalı/parolalı `staff_users`** tablosu var | **3) Personel hesaplarını kullanıcılara bağlama** |
| Önceki sürümü kurdunuz: `users` tablosunda **`role` kolonu** var | **4) `users.role` kolonundan taşıma** |
| Test verilerini temizlemek istiyorsunuz | **5) Verileri sıfırlama** |

## Hesap tabloları (önemli)

Bir kişi aynı anda **müşteri, personel ve yönetici** olabilir; bu yüzden **tek hesap, tek e-posta, tek parola** vardır:

| Tablo | Ne tutar |
|-------|----------|
| `users` | **Herkes**: e-posta, parola özeti, ad, telefon/adres (şifreli), aktiflik. Kayıt olan herkes buraya düşer (müşteri). |
| `staff_users` | **Yalnızca yetki kaydı**: hangi kişi (`user_id` → `users.id`) personel/yönetici ve hangi yetkilere sahip. |

* Yetki kaydı olmayan kişi **müşteri**dir. Yetki kaydı olan kişi hem müşteri hem personel/yönetici olarak aynı hesapla, **tek girişle** çalışır
  (alışveriş yapar, yetkileri kadar yönetim yapar). Yetkiyi kaldırmak = `staff_users` kaydını silmek; kişi yine müşteri olarak kalır.
* Rol ve yetkiler yalnızca `staff_users` içindedir. İlk yönetici SQL ile değil, backend ilk açıldığında oluşturulur (bkz. README).
* Tüm tarih alanları uygulama tarafından **UTC** yazılır. Seed verisi `UTC_TIMESTAMP()` kullanır.
* Stok tutarlılığı `CHECK` kısıtlarıyla da korunur: stok **asla eksiye düşemez**, `physical = reserved + available` bozulamaz.
* **Şifrelenen/özetlenen alanlar:** parola özetleri (PBKDF2), `users.phone_enc`, `users.address_enc`,
  `payment_cards.holder_name_enc`, `payment_cards.card_number_enc`, `customer_orders.shipping_address_enc`
  (AES-256-GCM, uygulama tarafında; anahtar veritabanında **tutulmaz**). `*_enc` kolonlarını phpMyAdmin'de okuyamazsınız; bu beklenen davranıştır.
  **CVV hiçbir yerde saklanmaz.**

## 1) Sıfırdan kurulum

```sql
CREATE DATABASE IF NOT EXISTS high_traffic_stock
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE high_traffic_stock;

-- ---------------------------------------------------------------
-- products
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS products (
  id              INT            NOT NULL AUTO_INCREMENT,
  name            VARCHAR(150)   NOT NULL,
  sku             VARCHAR(50)    NOT NULL,
  barcode         VARCHAR(50)    NOT NULL,                 -- mobil kameradan okutulacak barkod
  price           DECIMAL(10,2)  NOT NULL,
  physical_stock  INT            NOT NULL,                 -- depodaki toplam gerçek stok
  reserved_stock  INT            NOT NULL DEFAULT 0,       -- sepette/rezervede bekleyen stok
  available_stock INT            NOT NULL,                 -- satılabilir anlık stok (physical - reserved)
  version         INT            NOT NULL DEFAULT 1,       -- optimistic locking / olay sıralama versiyonu
  created_at      DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at      DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE INDEX ux_products_sku (sku),
  UNIQUE INDEX ux_products_barcode (barcode),
  CONSTRAINT chk_products_price     CHECK (price >= 0),
  CONSTRAINT chk_products_physical  CHECK (physical_stock >= 0),
  CONSTRAINT chk_products_reserved  CHECK (reserved_stock >= 0),
  CONSTRAINT chk_products_available CHECK (available_stock >= 0),
  CONSTRAINT chk_products_balance   CHECK (physical_stock = reserved_stock + available_stock)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------
-- users (HERKES: müşteri, personel, yönetici – tek hesap)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
  id                 CHAR(36)     NOT NULL,                -- UUID
  email              VARCHAR(190) NOT NULL,                -- küçük harfe çevrilmiş, giriş için
  password_hash      VARCHAR(255) NOT NULL,                -- PBKDF2-SHA256 (tuzlu)
  full_name          VARCHAR(150) NOT NULL,
  phone_enc          VARCHAR(512) NULL,                    -- AES-256-GCM
  address_enc        TEXT         NULL,                    -- AES-256-GCM (JSON, varsayılan teslimat adresi)
  is_active          TINYINT(1)   NOT NULL DEFAULT 1,      -- 0 = hesap devre dışı (giriş ve mevcut token'lar reddedilir)
  failed_login_count INT          NOT NULL DEFAULT 0,
  locked_until       DATETIME     NULL,
  created_at         DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_login_at      DATETIME     NULL,
  PRIMARY KEY (id),
  UNIQUE INDEX ux_users_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------
-- staff_users (PERSONEL/YÖNETİCİ YETKİ KAYDI – users'a bağlı, ayrı tablo)
-- Kaydı olan kişi personel/yönetici de olur; kaydı silinen yine müşteri olarak kalır.
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS staff_users (
  user_id     CHAR(36)     NOT NULL,                       -- users.id
  role        ENUM('STAFF','ADMIN') NOT NULL DEFAULT 'STAFF',  -- ADMIN: tüm yetkiler
  permissions VARCHAR(500) NULL,                           -- STAFF için virgülle ayrılmış yetkiler (örn. stock.view,stock.restock)
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by  CHAR(36)     NULL,                           -- yetkiyi veren yönetici (users.id)
  PRIMARY KEY (user_id),
  CONSTRAINT fk_staff_users_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------
-- reservations (sepetteki ürünler = müşterinin ACTIVE rezervasyonları)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS reservations (
  id                 CHAR(36)     NOT NULL,                -- UUID
  product_id         INT          NOT NULL,
  barcode            VARCHAR(50)  NOT NULL,
  session_or_user_id VARCHAR(100) NOT NULL,                -- müşteri için users.id
  quantity           INT          NOT NULL,
  status             ENUM('ACTIVE','CONFIRMED','EXPIRED','CANCELLED') NOT NULL DEFAULT 'ACTIVE',
  expires_at         DATETIME     NOT NULL,                -- oluşturulma + 5 dk
  created_at         DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  INDEX ix_reservations_status_expires (status, expires_at),
  INDEX ix_reservations_user_status (session_or_user_id, status),
  INDEX ix_reservations_product (product_id),
  INDEX ix_reservations_barcode (barcode),
  INDEX ix_reservations_created (created_at),
  CONSTRAINT fk_reservations_product FOREIGN KEY (product_id) REFERENCES products (id),
  CONSTRAINT chk_reservations_qty CHECK (quantity > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------
-- customer_orders (müşteri siparişi başlığı: ödeme + teslimat bilgisi)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS customer_orders (
  id                   CHAR(36)      NOT NULL,             -- UUID
  order_no             VARCHAR(30)   NOT NULL,             -- müşteriye gösterilen sipariş numarası
  user_id              CHAR(36)      NOT NULL,
  total_price          DECIMAL(10,2) NOT NULL,
  status               ENUM('PAID','REFUNDED') NOT NULL DEFAULT 'PAID',
  payment_brand        VARCHAR(20)   NOT NULL,             -- Visa / Mastercard ...
  payment_last4        CHAR(4)       NOT NULL,             -- kartın yalnızca son 4 hanesi
  payment_ref          VARCHAR(60)   NOT NULL,             -- ödeme sağlayıcı işlem referansı
  shipping_address_enc TEXT          NOT NULL,             -- AES-256-GCM (JSON)
  created_at           DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE INDEX ux_customer_orders_no (order_no),
  INDEX ix_customer_orders_user (user_id, created_at),
  CONSTRAINT fk_customer_orders_user FOREIGN KEY (user_id) REFERENCES users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------
-- orders (sipariş kalemleri; her rezervasyon en fazla bir kez siparişe dönüşür)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS orders (
  id                CHAR(36)      NOT NULL,                -- UUID
  reservation_id    CHAR(36)      NOT NULL,
  product_id        INT           NOT NULL,
  user_id           VARCHAR(100)  NOT NULL,
  quantity          INT           NOT NULL,
  total_price       DECIMAL(10,2) NOT NULL,
  created_at        DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  customer_order_id CHAR(36)      NULL,                    -- mağaza siparişi; yönetici onaylarında NULL
  PRIMARY KEY (id),
  UNIQUE INDEX ux_orders_reservation (reservation_id),
  INDEX ix_orders_product (product_id),
  INDEX ix_orders_created (created_at),
  INDEX ix_orders_customer_order (customer_order_id),
  CONSTRAINT fk_orders_reservation     FOREIGN KEY (reservation_id)    REFERENCES reservations (id),
  CONSTRAINT fk_orders_product         FOREIGN KEY (product_id)        REFERENCES products (id),
  CONSTRAINT fk_orders_customer_order  FOREIGN KEY (customer_order_id) REFERENCES customer_orders (id),
  CONSTRAINT chk_orders_qty CHECK (quantity > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------
-- stock_movement_logs (audit trail)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS stock_movement_logs (
  id               BIGINT       NOT NULL AUTO_INCREMENT,
  product_id       INT          NOT NULL,
  barcode          VARCHAR(50)  NOT NULL,
  action_type      ENUM('RESERVE','CONFIRM_ORDER','EXPIRE_ROLLBACK','CANCEL','MANUAL_RESTOCK') NOT NULL,
  quantity_changed INT          NOT NULL,
  description      VARCHAR(255) NULL,
  created_at       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  INDEX ix_logs_product_created (product_id, created_at),
  INDEX ix_logs_created (created_at),
  CONSTRAINT fk_logs_product FOREIGN KEY (product_id) REFERENCES products (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------
-- payment_cards (müşterinin kayıtlı kartları – CVV SAKLANMAZ)
-- ---------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payment_cards (
  id              CHAR(36)     NOT NULL,                   -- UUID
  user_id         CHAR(36)     NOT NULL,
  brand           VARCHAR(20)  NOT NULL,
  last4           CHAR(4)      NOT NULL,
  holder_name_enc VARCHAR(512) NOT NULL,                   -- AES-256-GCM
  card_number_enc VARCHAR(512) NOT NULL,                   -- AES-256-GCM
  fingerprint     CHAR(64)     NOT NULL,                   -- HMAC-SHA256(kart no): aynı kartı tekrar kaydetmeyi engeller
  exp_month       TINYINT      NOT NULL,
  exp_year        SMALLINT     NOT NULL,
  created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE INDEX ux_payment_cards_user_fp (user_id, fingerprint),
  CONSTRAINT fk_payment_cards_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------
-- Seed: 5 örnek ürün (ilk ürünün stoğu 5 -> race condition testi için ideal)
-- ---------------------------------------------------------------
INSERT IGNORE INTO products
  (name, sku, barcode, price, physical_stock, reserved_stock, available_stock, version, created_at, updated_at)
VALUES
  ('Kablosuz Kulaklık Pro (Flash Sale)', 'SKU-HP-001', '8690000000011',  1499.90,   5, 0,   5, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  ('Akıllı Saat Series 7',               'SKU-SW-002', '8690000000028',  3299.00,  50, 0,  50, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  ('Mekanik Klavye RGB',                 'SKU-KB-003', '8690000000035',   899.50, 120, 0, 120, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  ('4K Oyuncu Monitörü 27"',             'SKU-MN-004', '8690000000042',  7999.00,  20, 0,  20, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  ('Taşınabilir SSD 1TB',                'SKU-SD-005', '8690000000059',  2149.75,  10, 0,  10, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP());
```

## 2) Faz 1'den yükseltme

Yalnızca Faz 1 tablolarını (`products`, `reservations`, `orders`, `stock_movement_logs`) oluşturduysanız bunu çalıştırın.
Mevcut verilere dokunmaz. `ALTER` komutları tek seferliktir; tekrar çalıştırırsanız "Duplicate column / key name" hatası alırsınız, zararsızdır.

```sql
USE high_traffic_stock;

CREATE TABLE IF NOT EXISTS users (
  id                 CHAR(36)     NOT NULL,
  email              VARCHAR(190) NOT NULL,
  password_hash      VARCHAR(255) NOT NULL,
  full_name          VARCHAR(150) NOT NULL,
  phone_enc          VARCHAR(512) NULL,
  address_enc        TEXT         NULL,
  is_active          TINYINT(1)   NOT NULL DEFAULT 1,
  failed_login_count INT          NOT NULL DEFAULT 0,
  locked_until       DATETIME     NULL,
  created_at         DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_login_at      DATETIME     NULL,
  PRIMARY KEY (id),
  UNIQUE INDEX ux_users_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS staff_users (
  user_id     CHAR(36)     NOT NULL,
  role        ENUM('STAFF','ADMIN') NOT NULL DEFAULT 'STAFF',
  permissions VARCHAR(500) NULL,
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by  CHAR(36)     NULL,
  PRIMARY KEY (user_id),
  CONSTRAINT fk_staff_users_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS customer_orders (
  id                   CHAR(36)      NOT NULL,
  order_no             VARCHAR(30)   NOT NULL,
  user_id              CHAR(36)      NOT NULL,
  total_price          DECIMAL(10,2) NOT NULL,
  status               ENUM('PAID','REFUNDED') NOT NULL DEFAULT 'PAID',
  payment_brand        VARCHAR(20)   NOT NULL,
  payment_last4        CHAR(4)       NOT NULL,
  payment_ref          VARCHAR(60)   NOT NULL,
  shipping_address_enc TEXT          NOT NULL,
  created_at           DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE INDEX ux_customer_orders_no (order_no),
  INDEX ix_customer_orders_user (user_id, created_at),
  CONSTRAINT fk_customer_orders_user FOREIGN KEY (user_id) REFERENCES users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS payment_cards (
  id              CHAR(36)     NOT NULL,
  user_id         CHAR(36)     NOT NULL,
  brand           VARCHAR(20)  NOT NULL,
  last4           CHAR(4)      NOT NULL,
  holder_name_enc VARCHAR(512) NOT NULL,
  card_number_enc VARCHAR(512) NOT NULL,
  fingerprint     CHAR(64)     NOT NULL,
  exp_month       TINYINT      NOT NULL,
  exp_year        SMALLINT     NOT NULL,
  created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE INDEX ux_payment_cards_user_fp (user_id, fingerprint),
  CONSTRAINT fk_payment_cards_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE orders
  ADD COLUMN customer_order_id CHAR(36) NULL,
  ADD INDEX ix_orders_customer_order (customer_order_id),
  ADD CONSTRAINT fk_orders_customer_order FOREIGN KEY (customer_order_id) REFERENCES customer_orders (id);

ALTER TABLE reservations
  ADD INDEX ix_reservations_user_status (session_or_user_id, status);
```

## 3) Personel hesaplarını kullanıcılara bağlama (önceki sürümden)

Bir önceki sürümü kurduysanız (yani `staff_users` tablosunda **kendi e-posta ve parolası olan** personel/yönetici hesapları varsa) **yalnızca bunu** çalıştırın.
Artık herkes `users` tablosunda tek hesaba sahiptir; `staff_users` yalnızca yetki kaydı olur:

* eski `staff_users` tablosunu yedek olarak `staff_users_old` adına alır,
* `users` içinde **aynı e-postası olmayan** personelleri `users` tablosuna taşır (aynı kimlik ve **aynı parola özetiyle**; parolalar değişmez),
* her personel/yöneticiyi (aktif olanları) kendi `users` kaydına bağlayan yeni `staff_users` kayıtları oluşturur,
* eski tabloyu siler.

> Aynı e-postayla hem müşteri hem personel hesabınız varsa **müşteri hesabının parolası** geçerli kalır, personel parolası artık kullanılmaz
> (yetkiler müşteri hesabınıza bağlanır). Devre dışı bırakılmış personel hesapları yetkisiz (müşteri) olarak aktarılır.

```sql
USE high_traffic_stock;

RENAME TABLE staff_users TO staff_users_old;

CREATE TABLE staff_users (
  user_id     CHAR(36)     NOT NULL,
  role        ENUM('STAFF','ADMIN') NOT NULL DEFAULT 'STAFF',
  permissions VARCHAR(500) NULL,
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by  CHAR(36)     NULL,
  PRIMARY KEY (user_id),
  CONSTRAINT fk_staff_users_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO users
  (id, email, password_hash, full_name, is_active, failed_login_count, locked_until, created_at, last_login_at)
SELECT o.id, o.email, o.password_hash, o.full_name, o.is_active, o.failed_login_count, o.locked_until, o.created_at, o.last_login_at
FROM staff_users_old o
WHERE o.email NOT IN (SELECT email FROM users);

INSERT IGNORE INTO staff_users (user_id, role, permissions, created_at, created_by)
SELECT u.id, o.role, o.permissions, o.created_at, o.created_by
FROM staff_users_old o
JOIN users u ON u.email = o.email
WHERE o.is_active = 1;

DROP TABLE staff_users_old;
```

## 4) `users.role` kolonundan taşıma (önceki sürümden)

`users` tablonuzda `role` ve `permissions` kolonları varsa (ayrı personel tablosu olmayan sürüm) **yalnızca bunu** çalıştırın.
Personel/yöneticileri yeni `staff_users` yetki tablosuna alır ve kolonları kaldırır; hesaplar ve parolalar `users` içinde aynen kalır.
(`permissions` kolonu yoksa önce `ALTER TABLE users ADD COLUMN permissions VARCHAR(500) NULL;` çalıştırın.)

```sql
USE high_traffic_stock;

CREATE TABLE IF NOT EXISTS staff_users (
  user_id     CHAR(36)     NOT NULL,
  role        ENUM('STAFF','ADMIN') NOT NULL DEFAULT 'STAFF',
  permissions VARCHAR(500) NULL,
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by  CHAR(36)     NULL,
  PRIMARY KEY (user_id),
  CONSTRAINT fk_staff_users_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO staff_users (user_id, role, permissions, created_at)
SELECT id, role, permissions, created_at
FROM users
WHERE role IN ('ADMIN', 'STAFF');

ALTER TABLE users
  DROP COLUMN role,
  DROP COLUMN permissions;
```

## 5) (Opsiyonel) Verileri sıfırlama

Stokları, rezervasyonları, siparişleri ve logları başlangıç haline döndürür. **Müşteriler, personel ve kayıtlı kartlar silinmez.**

```sql
USE high_traffic_stock;

DELETE FROM orders;
DELETE FROM customer_orders;
DELETE FROM stock_movement_logs;
DELETE FROM reservations;

UPDATE products SET physical_stock = 5,   reserved_stock = 0, available_stock = 5,   version = 1, updated_at = UTC_TIMESTAMP() WHERE barcode = '8690000000011';
UPDATE products SET physical_stock = 50,  reserved_stock = 0, available_stock = 50,  version = 1, updated_at = UTC_TIMESTAMP() WHERE barcode = '8690000000028';
UPDATE products SET physical_stock = 120, reserved_stock = 0, available_stock = 120, version = 1, updated_at = UTC_TIMESTAMP() WHERE barcode = '8690000000035';
UPDATE products SET physical_stock = 20,  reserved_stock = 0, available_stock = 20,  version = 1, updated_at = UTC_TIMESTAMP() WHERE barcode = '8690000000042';
UPDATE products SET physical_stock = 10,  reserved_stock = 0, available_stock = 10,  version = 1, updated_at = UTC_TIMESTAMP() WHERE barcode = '8690000000059';
```

## Seed barkodları

| Ürün | Barkod | Fiyat | Başlangıç stoğu |
|------|--------|-------|-----------------|
| Kablosuz Kulaklık Pro (Flash Sale) | `8690000000011` | 1499.90 | 5 |
| Akıllı Saat Series 7 | `8690000000028` | 3299.00 | 50 |
| Mekanik Klavye RGB | `8690000000035` | 899.50 | 120 |
| 4K Oyuncu Monitörü 27" | `8690000000042` | 7999.00 | 20 |
| Taşınabilir SSD 1TB | `8690000000059` | 2149.75 | 10 |
