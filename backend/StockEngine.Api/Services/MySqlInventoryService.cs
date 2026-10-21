using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;
using StockEngine.Api.Models;

namespace StockEngine.Api.Services;

/// <summary>
/// MySQL native atomik implementasyon (Seçenek B).
///
/// Sıfır overselling garantisi tek bir koşullu UPDATE ile sağlanır:
///   UPDATE products SET available_stock = available_stock - @qty, reserved_stock = reserved_stock + @qty
///   WHERE barcode = @barcode AND available_stock >= @qty
/// InnoDB bu satırı satır bazlı (row-level) kilitler; aynı ürüne gelen eşzamanlı istekler sıraya girer ve
/// koşul her istek için güncel değer üzerinden değerlendirilir. Etkilenen satır sayısı 0 ise stok yetersizdir.
///
/// Kilit sırası her akışta aynıdır (önce reservations, sonra products) ve deadlock durumunda işlem yeniden denenir.
/// </summary>
public class MySqlInventoryService : IInventoryService
{
    private const int MaxQuantity = 1000;
    private const int MaxRestock = 1_000_000;

    private readonly Database _db;
    private readonly ReservationOptions _options;

    public MySqlInventoryService(Database db, IOptions<ReservationOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    // ---------------------------------------------------------------- okuma

    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<Product>(
            new CommandDefinition("SELECT * FROM products ORDER BY id", cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Product?> GetProductByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Product>(new CommandDefinition(
            "SELECT * FROM products WHERE barcode = @barcode", new { barcode = barcode.Trim() }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ReservationRow>> GetReservationsAsync(int limit, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<ReservationRow>(new CommandDefinition(
            @"SELECT r.id, r.product_id, r.barcode, r.session_or_user_id, r.quantity, r.status,
                     r.expires_at, r.created_at, p.name AS product_name
              FROM reservations r
              JOIN products p ON p.id = r.product_id
              ORDER BY r.created_at DESC, r.id
              LIMIT @limit",
            new { limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<StockLog>> GetLogsAsync(int limit, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<StockLog>(new CommandDefinition(
            "SELECT * FROM stock_movement_logs ORDER BY id DESC LIMIT @limit",
            new { limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct));
        return rows.AsList();
    }

    // ---------------------------------------------------------------- rezerve

    public async Task<OpResult> ReserveAsync(string? barcode, int quantity, string? userId, CancellationToken ct = default)
    {
        barcode = barcode?.Trim();
        userId = userId?.Trim();
        if (string.IsNullOrEmpty(barcode) || barcode.Length > 50)
            return OpResult.Fail(OpStatus.Invalid, "Geçerli bir barkod gereklidir.");
        if (quantity < 1 || quantity > MaxQuantity)
            return OpResult.Fail(OpStatus.Invalid, $"Miktar 1 ile {MaxQuantity} arasında olmalıdır.");
        if (string.IsNullOrEmpty(userId) || userId.Length > 100)
            return OpResult.Fail(OpStatus.Invalid, "Geçerli bir userId gereklidir (en fazla 100 karakter).");

        return await InTransactionAsync(async (conn, tx) =>
        {
            var now = UtcNowSeconds();

            // Atomik stok düşümü: koşul ve düşüm tek ifadede, satır kilidi altında.
            var affected = await conn.ExecuteAsync(
                @"UPDATE products
                  SET available_stock = available_stock - @quantity,
                      reserved_stock  = reserved_stock + @quantity,
                      version = version + 1,
                      updated_at = @now
                  WHERE barcode = @barcode AND available_stock >= @quantity",
                new { quantity, barcode, now }, tx);

            if (affected == 0)
            {
                var current = await conn.QuerySingleOrDefaultAsync<Product>(
                    "SELECT * FROM products WHERE barcode = @barcode", new { barcode }, tx);
                return current is null
                    ? OpResult.Fail(OpStatus.ProductNotFound, "Ürün bulunamadı.")
                    : OpResult.Fail(OpStatus.InsufficientStock, "Yetersiz stok.", current.AvailableStock);
            }

            var product = await conn.QuerySingleAsync<Product>(
                "SELECT * FROM products WHERE barcode = @barcode", new { barcode }, tx);

            var reservation = new ReservationRow
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Barcode = product.Barcode,
                SessionOrUserId = userId,
                Quantity = quantity,
                Status = ReservationStatus.Active,
                ExpiresAt = now.AddMinutes(_options.TtlMinutes),
                CreatedAt = now,
                ProductName = product.Name
            };

            await conn.ExecuteAsync(
                @"INSERT INTO reservations (id, product_id, barcode, session_or_user_id, quantity, status, expires_at, created_at)
                  VALUES (@Id, @ProductId, @Barcode, @SessionOrUserId, @Quantity, @Status, @ExpiresAt, @CreatedAt)",
                reservation, tx);

            var log = await InsertLogAsync(conn, tx, product, StockAction.Reserve, -quantity,
                $"{quantity} adet rezerve edildi (kullanıcı: {userId})", now);

            return new OpResult
            {
                Status = OpStatus.Ok,
                Message = "Rezervasyon oluşturuldu.",
                Product = product,
                Reservation = reservation,
                Logs = { log }
            };
        }, ct);
    }

    // ---------------------------------------------------------------- sipariş onayı

    public async Task<OpResult> ConfirmAsync(Guid reservationId, CancellationToken ct = default)
    {
        if (reservationId == Guid.Empty)
            return OpResult.Fail(OpStatus.Invalid, "Geçerli bir reservationId gereklidir.");

        return await InTransactionAsync(async (conn, tx) =>
        {
            var reservation = await LockReservationAsync(conn, tx, reservationId);
            if (reservation is null)
                return OpResult.Fail(OpStatus.ReservationNotFound, "Rezervasyon bulunamadı.");

            if (reservation.Status != ReservationStatus.Active)
                return OpResult.Fail(OpStatus.NotActive, $"Rezervasyon aktif değil (durum: {reservation.Status}).");

            var now = UtcNowSeconds();

            // Süresi dolmuşsa siparişe izin verme; stoğu hemen iade et.
            if (reservation.ExpiresAt <= DateTime.UtcNow)
            {
                var (expiredProduct, expiredLog) = await ReleaseAsync(conn, tx, reservation,
                    ReservationStatus.Expired, StockAction.ExpireRollback, +reservation.Quantity,
                    $"Süre dolduğu için rezervasyon iptal edildi, {reservation.Quantity} adet iade edildi", now);

                return new OpResult
                {
                    Status = OpStatus.Expired,
                    Message = "Rezervasyonun süresi dolmuş; stok iade edildi.",
                    Product = expiredProduct,
                    Reservation = reservation,
                    Logs = { expiredLog }
                };
            }

            return await ConfirmReservationAsync(conn, tx, reservation, null, now);
        }, ct);
    }

    // Aktif ve süresi dolmamış bir rezervasyonu siparişe çevirir (stok düşümü + orders + log).
    private static async Task<OpResult> ConfirmReservationAsync(
        MySqlConnection conn, MySqlTransaction tx, ReservationRow reservation, Guid? customerOrderId, DateTime now)
    {
        var affected = await conn.ExecuteAsync(
            @"UPDATE products
              SET physical_stock = physical_stock - @quantity,
                  reserved_stock = reserved_stock - @quantity,
                  version = version + 1,
                  updated_at = @now
              WHERE id = @productId AND reserved_stock >= @quantity AND physical_stock >= @quantity",
            new { quantity = reservation.Quantity, productId = reservation.ProductId, now }, tx);
        if (affected == 0)
            throw new InvalidOperationException($"Stok tutarsızlığı: ürün {reservation.ProductId}, rezervasyon {reservation.Id}.");

        await conn.ExecuteAsync(
            "UPDATE reservations SET status = @status WHERE id = @id AND status = 'ACTIVE'",
            new { status = ReservationStatus.Confirmed, id = reservation.Id }, tx);
        reservation.Status = ReservationStatus.Confirmed;

        var product = await conn.QuerySingleAsync<Product>(
            "SELECT * FROM products WHERE id = @id", new { id = reservation.ProductId }, tx);
        reservation.ProductName = product.Name;

        var order = new OrderRow
        {
            Id = Guid.NewGuid(),
            ReservationId = reservation.Id,
            ProductId = product.Id,
            UserId = reservation.SessionOrUserId,
            Quantity = reservation.Quantity,
            TotalPrice = product.Price * reservation.Quantity,
            CreatedAt = now,
            CustomerOrderId = customerOrderId
        };
        await conn.ExecuteAsync(
            @"INSERT INTO orders (id, reservation_id, product_id, user_id, quantity, total_price, created_at, customer_order_id)
              VALUES (@Id, @ReservationId, @ProductId, @UserId, @Quantity, @TotalPrice, @CreatedAt, @CustomerOrderId)",
            order, tx);

        var log = await InsertLogAsync(conn, tx, product, StockAction.ConfirmOrder, -reservation.Quantity,
            $"Sipariş onaylandı: {reservation.Quantity} adet, toplam {order.TotalPrice:0.00} (kullanıcı: {reservation.SessionOrUserId})", now);

        return new OpResult
        {
            Status = OpStatus.Ok,
            Message = "Sipariş onaylandı.",
            Product = product,
            Reservation = reservation,
            Order = order,
            Logs = { log }
        };
    }

    // ---------------------------------------------------------------- sepet ödemesi (checkout)

    public async Task<CheckoutResult> CheckoutAsync(
        string userId, CheckoutHeader header, decimal expectedTotal, CancellationToken ct = default)
    {
        return await InTransactionAsync(async (conn, tx) =>
        {
            // Kullanıcının tüm aktif rezervasyonları kilitlenir (kilit sırası: reservations -> products).
            var rows = (await conn.QueryAsync<ReservationRow>(
                @"SELECT id, product_id, barcode, session_or_user_id, quantity, status, expires_at, created_at
                  FROM reservations
                  WHERE session_or_user_id = @userId AND status = 'ACTIVE'
                  ORDER BY product_id, id FOR UPDATE",
                new { userId }, tx)).AsList();

            if (rows.Count == 0)
                return CheckoutResult.Fail(CheckoutStatus.EmptyCart, "Sepetiniz boş.");

            var now = UtcNowSeconds();
            var parts = new List<OpResult>();

            // Süresi dolanlar iade edilir; sepet değiştiği için ödeme iptal edilmelidir.
            var expired = rows.Where(r => r.ExpiresAt <= DateTime.UtcNow).ToList();
            if (expired.Count > 0)
            {
                foreach (var r in expired)
                {
                    var (p, l) = await ReleaseAsync(conn, tx, r, ReservationStatus.Expired, StockAction.ExpireRollback,
                        +r.Quantity, $"Sepet süresi doldu, {r.Quantity} adet stok iade edildi (kullanıcı: {userId})", now);
                    parts.Add(new OpResult { Status = OpStatus.Expired, Product = p, Reservation = r, Logs = { l } });
                }
                return CheckoutResult.Fail(CheckoutStatus.Expired,
                    "Sepetinizdeki bazı ürünlerin rezervasyon süresi doldu.", parts);
            }

            var productIds = rows.Select(r => r.ProductId).Distinct().ToArray();
            var prices = (await conn.QueryAsync<(int Id, decimal Price)>(
                "SELECT id AS Id, price AS Price FROM products WHERE id IN @productIds",
                new { productIds }, tx)).ToDictionary(x => x.Id, x => x.Price);

            var total = rows.Sum(r => prices[r.ProductId] * r.Quantity);
            if (total != expectedTotal)
                return CheckoutResult.Fail(CheckoutStatus.TotalMismatch, "Sepet tutarı değişti.");

            await conn.ExecuteAsync(
                @"INSERT INTO customer_orders
                    (id, order_no, user_id, total_price, status, payment_brand, payment_last4, payment_ref, shipping_address_enc, created_at)
                  VALUES (@Id, @OrderNo, @userId, @total, 'PAID', @PaymentBrand, @PaymentLast4, @PaymentRef, @ShippingAddressEnc, @now)",
                new
                {
                    header.Id, header.OrderNo, userId = Guid.Parse(userId), total,
                    header.PaymentBrand, header.PaymentLast4, header.PaymentRef, header.ShippingAddressEnc, now
                }, tx);

            foreach (var r in rows)
                parts.Add(await ConfirmReservationAsync(conn, tx, r, header.Id, now));

            return CheckoutResult.Success(
                new CustomerOrderRow
                {
                    Id = header.Id, OrderNo = header.OrderNo, UserId = Guid.Parse(userId), TotalPrice = total,
                    Status = "PAID", PaymentBrand = header.PaymentBrand, PaymentLast4 = header.PaymentLast4,
                    PaymentRef = header.PaymentRef, ShippingAddressEnc = header.ShippingAddressEnc, CreatedAt = now
                },
                parts);
        }, ct);
    }

    // ---------------------------------------------------------------- iptal

    public async Task<OpResult> CancelAsync(Guid reservationId, string? ownerUserId = null, CancellationToken ct = default)
    {
        if (reservationId == Guid.Empty)
            return OpResult.Fail(OpStatus.Invalid, "Geçerli bir reservationId gereklidir.");

        return await InTransactionAsync(async (conn, tx) =>
        {
            var reservation = await LockReservationAsync(conn, tx, reservationId);
            // Başkasının rezervasyonu "bulunamadı" gibi davranır (varlığı sızdırılmaz).
            if (reservation is null || (ownerUserId is not null && reservation.SessionOrUserId != ownerUserId))
                return OpResult.Fail(OpStatus.ReservationNotFound, "Rezervasyon bulunamadı.");

            if (reservation.Status != ReservationStatus.Active)
                return OpResult.Fail(OpStatus.NotActive, $"Rezervasyon aktif değil (durum: {reservation.Status}).");

            var (product, log) = await ReleaseAsync(conn, tx, reservation,
                ReservationStatus.Cancelled, StockAction.Cancel, +reservation.Quantity,
                $"Rezervasyon iptal edildi, {reservation.Quantity} adet iade edildi (kullanıcı: {reservation.SessionOrUserId})",
                UtcNowSeconds());

            return new OpResult
            {
                Status = OpStatus.Ok,
                Message = "Rezervasyon iptal edildi.",
                Product = product,
                Reservation = reservation,
                Logs = { log }
            };
        }, ct);
    }

    // ---------------------------------------------------------------- restock

    public async Task<OpResult> RestockAsync(string? barcode, int addQuantity, CancellationToken ct = default)
    {
        barcode = barcode?.Trim();
        if (string.IsNullOrEmpty(barcode) || barcode.Length > 50)
            return OpResult.Fail(OpStatus.Invalid, "Geçerli bir barkod gereklidir.");
        if (addQuantity < 1 || addQuantity > MaxRestock)
            return OpResult.Fail(OpStatus.Invalid, $"addQuantity 1 ile {MaxRestock} arasında olmalıdır.");

        return await InTransactionAsync(async (conn, tx) =>
        {
            var now = UtcNowSeconds();
            var affected = await conn.ExecuteAsync(
                @"UPDATE products
                  SET physical_stock = physical_stock + @addQuantity,
                      available_stock = available_stock + @addQuantity,
                      version = version + 1,
                      updated_at = @now
                  WHERE barcode = @barcode",
                new { addQuantity, barcode, now }, tx);
            if (affected == 0)
                return OpResult.Fail(OpStatus.ProductNotFound, "Ürün bulunamadı.");

            var product = await conn.QuerySingleAsync<Product>(
                "SELECT * FROM products WHERE barcode = @barcode", new { barcode }, tx);
            var log = await InsertLogAsync(conn, tx, product, StockAction.ManualRestock, addQuantity,
                $"Depoya {addQuantity} adet stok eklendi", now);

            return new OpResult { Status = OpStatus.Ok, Message = "Stok eklendi.", Product = product, Logs = { log } };
        }, ct);
    }

    // ---------------------------------------------------------------- süresi dolanlar

    public async Task<IReadOnlyList<OpResult>> ExpireDueReservationsAsync(CancellationToken ct = default)
    {
        List<Guid> dueIds;
        await using (var conn = await OpenAsync(ct))
        {
            var ids = await conn.QueryAsync<Guid>(new CommandDefinition(
                @"SELECT id FROM reservations
                  WHERE status = 'ACTIVE' AND expires_at <= @now
                  ORDER BY expires_at LIMIT 200",
                new { now = DateTime.UtcNow }, cancellationToken: ct));
            dueIds = ids.AsList();
        }

        var results = new List<OpResult>(dueIds.Count);
        foreach (var id in dueIds)
        {
            var result = await InTransactionAsync<OpResult?>(async (conn, tx) =>
            {
                var reservation = await LockReservationAsync(conn, tx, id);
                // Bu arada onaylanmış/iptal edilmiş olabilir.
                if (reservation is null || reservation.Status != ReservationStatus.Active
                    || reservation.ExpiresAt > DateTime.UtcNow)
                    return null;

                var (product, log) = await ReleaseAsync(conn, tx, reservation,
                    ReservationStatus.Expired, StockAction.ExpireRollback, +reservation.Quantity,
                    $"Rezervasyon süresi doldu, {reservation.Quantity} adet stok iade edildi (kullanıcı: {reservation.SessionOrUserId})",
                    UtcNowSeconds());

                return new OpResult
                {
                    Status = OpStatus.Ok,
                    Message = "Rezervasyon süresi doldu.",
                    Product = product,
                    Reservation = reservation,
                    Logs = { log }
                };
            }, ct);

            if (result is not null) results.Add(result);
        }
        return results;
    }

    // ---------------------------------------------------------------- yardımcılar

    private static DateTime UtcNowSeconds()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    private Task<MySqlConnection> OpenAsync(CancellationToken ct) => _db.OpenAsync(ct);

    private Task<T> InTransactionAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> work, CancellationToken ct)
        => _db.InTransactionAsync(work, ct);

    // Rezervasyon satırını kilitler. Ürün adı için join kullanılmaz: kilit sırası (reservations -> products) sabit kalsın.
    private static async Task<ReservationRow?> LockReservationAsync(MySqlConnection conn, MySqlTransaction tx, Guid id)
    {
        var reservation = await conn.QuerySingleOrDefaultAsync<ReservationRow>(
            @"SELECT id, product_id, barcode, session_or_user_id, quantity, status, expires_at, created_at
              FROM reservations WHERE id = @id FOR UPDATE",
            new { id }, tx);
        if (reservation is null) return null;

        reservation.ProductName = await conn.ExecuteScalarAsync<string>(
            "SELECT name FROM products WHERE id = @id", new { id = reservation.ProductId }, tx) ?? "";
        return reservation;
    }

    // ACTIVE bir rezervasyonu kapatır (EXPIRED/CANCELLED) ve rezerve stoğu satılabilir stoğa geri aktarır.
    private static async Task<(Product Product, StockLog Log)> ReleaseAsync(
        MySqlConnection conn, MySqlTransaction tx, ReservationRow reservation,
        string newStatus, string action, int quantityChanged, string description, DateTime now)
    {
        await conn.ExecuteAsync(
            "UPDATE reservations SET status = @status WHERE id = @id AND status = 'ACTIVE'",
            new { status = newStatus, id = reservation.Id }, tx);
        reservation.Status = newStatus;

        var affected = await conn.ExecuteAsync(
            @"UPDATE products
              SET reserved_stock = reserved_stock - @quantity,
                  available_stock = available_stock + @quantity,
                  version = version + 1,
                  updated_at = @now
              WHERE id = @productId AND reserved_stock >= @quantity",
            new { quantity = reservation.Quantity, productId = reservation.ProductId, now }, tx);
        if (affected == 0)
            throw new InvalidOperationException($"Stok tutarsızlığı: ürün {reservation.ProductId}, rezervasyon {reservation.Id}.");

        var product = await conn.QuerySingleAsync<Product>(
            "SELECT * FROM products WHERE id = @id", new { id = reservation.ProductId }, tx);
        var log = await InsertLogAsync(conn, tx, product, action, quantityChanged, description, now);
        return (product, log);
    }

    private static async Task<StockLog> InsertLogAsync(
        MySqlConnection conn, MySqlTransaction tx, Product product,
        string action, int quantityChanged, string description, DateTime now)
    {
        if (description.Length > 255) description = description[..255];

        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO stock_movement_logs (product_id, barcode, action_type, quantity_changed, description, created_at)
              VALUES (@productId, @barcode, @action, @quantityChanged, @description, @now);
              SELECT LAST_INSERT_ID();",
            new { productId = product.Id, barcode = product.Barcode, action, quantityChanged, description, now }, tx);

        return new StockLog
        {
            Id = id,
            ProductId = product.Id,
            Barcode = product.Barcode,
            ActionType = action,
            QuantityChanged = quantityChanged,
            Description = description,
            CreatedAt = now
        };
    }
}
