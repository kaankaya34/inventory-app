using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using StockEngine.Api.Models;
using StockEngine.Api.Security;
using StockEngine.Api.Services.Payments;

namespace StockEngine.Api.Services;

// Mağaza akışı: sepet (= kullanıcının ACTIVE rezervasyonları), ödeme, siparişler ve kayıtlı kartlar.
public class ShopService
{
    private const int MaxQtyPerAdd = 10;

    private readonly Database _db;
    private readonly IInventoryService _inventory;
    private readonly IStockNotifier _notifier;
    private readonly IEncryptionService _crypto;
    private readonly IPaymentGateway _gateway;
    private readonly AuthService _auth;
    private readonly ILogger<ShopService> _logger;

    public ShopService(
        Database db, IInventoryService inventory, IStockNotifier notifier, IEncryptionService crypto,
        IPaymentGateway gateway, AuthService auth, ILogger<ShopService> logger)
    {
        _db = db;
        _inventory = inventory;
        _notifier = notifier;
        _crypto = crypto;
        _gateway = gateway;
        _auth = auth;
        _logger = logger;
    }

    // ------------------------------------------------------------ sepet

    public async Task<CartDto> GetCartAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<CartRow>(new CommandDefinition(
            @"SELECT r.id AS reservation_id, r.product_id, p.name AS product_name, p.barcode, p.sku,
                     p.price AS unit_price, r.quantity, r.expires_at
              FROM reservations r
              JOIN products p ON p.id = r.product_id
              WHERE r.session_or_user_id = @userId AND r.status = 'ACTIVE' AND r.expires_at > @now
              ORDER BY r.created_at, r.id",
            new { userId = userId.ToString(), now = DateTime.UtcNow }, cancellationToken: ct));

        var now = DateTime.UtcNow;
        var lines = rows.Select(r => new CartLineDto(
            r.ReservationId, r.ProductId, r.ProductName, r.Barcode, r.Sku, r.UnitPrice, r.Quantity,
            r.UnitPrice * r.Quantity, r.ExpiresAt, Math.Max(0, (int)Math.Ceiling((r.ExpiresAt - now).TotalSeconds)))).ToList();
        return new CartDto(lines, lines.Sum(l => l.LineTotal), lines.Sum(l => l.Quantity));
    }

    public async Task<ServiceResult<CartDto>> AddToCartAsync(Guid userId, AddToCartRequest req, CancellationToken ct = default)
    {
        if (req.Quantity < 1 || req.Quantity > MaxQtyPerAdd)
            return ServiceResult<CartDto>.Fail(400, "INVALID_REQUEST", $"Miktar 1 ile {MaxQtyPerAdd} arasında olmalıdır.");

        var result = await _inventory.ReserveAsync(req.Barcode, req.Quantity, userId.ToString(), ct);
        if (!result.IsOk)
            return ServiceResult<CartDto>.Fail(StatusOf(result.Status), CodeOf(result.Status),
                result.Status == OpStatus.InsufficientStock ? $"Yetersiz stok (kalan: {result.AvailableStock ?? 0})." : result.Message);

        await _notifier.PublishAsync(result, ct: ct);
        return ServiceResult<CartDto>.Success(await GetCartAsync(userId, ct));
    }

    public async Task<ServiceResult<CartDto>> RemoveFromCartAsync(Guid userId, Guid reservationId, CancellationToken ct = default)
    {
        var result = await _inventory.CancelAsync(reservationId, userId.ToString(), ct);
        if (!result.IsOk && result.Status != OpStatus.NotActive)
            return ServiceResult<CartDto>.Fail(StatusOf(result.Status), CodeOf(result.Status), result.Message);

        if (result.IsOk) await _notifier.PublishAsync(result, ct: ct);
        return ServiceResult<CartDto>.Success(await GetCartAsync(userId, ct));
    }

    // ------------------------------------------------------------ kayıtlı kartlar

    public async Task<IReadOnlyList<SavedCardDto>> GetCardsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<PaymentCardRow>(new CommandDefinition(
            "SELECT * FROM payment_cards WHERE user_id = @userId ORDER BY created_at DESC", new { userId }, cancellationToken: ct));
        return rows.Select(r => new SavedCardDto(
            r.Id, r.Brand, r.Last4, _crypto.Decrypt(r.HolderNameEnc, "card.holder"), r.ExpMonth, r.ExpYear)).ToList();
    }

    public async Task<bool> DeleteCardAsync(Guid userId, Guid cardId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM payment_cards WHERE id = @cardId AND user_id = @userId", new { cardId, userId }, cancellationToken: ct)) > 0;
    }

    // ------------------------------------------------------------ siparişler

    public async Task<IReadOnlyList<CustomerOrderDto>> GetOrdersAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var orders = (await conn.QueryAsync<CustomerOrderRow>(new CommandDefinition(
            "SELECT * FROM customer_orders WHERE user_id = @userId ORDER BY created_at DESC, id LIMIT 50",
            new { userId }, cancellationToken: ct))).AsList();
        if (orders.Count == 0) return Array.Empty<CustomerOrderDto>();

        var ids = orders.Select(o => o.Id).ToArray();
        var items = (await conn.QueryAsync<OrderItemRow>(new CommandDefinition(
            @"SELECT o.customer_order_id, o.product_id, p.name AS product_name, p.barcode,
                     o.quantity, o.total_price AS line_total
              FROM orders o JOIN products p ON p.id = o.product_id
              WHERE o.customer_order_id IN @ids ORDER BY o.created_at, o.id",
            new { ids }, cancellationToken: ct))).ToLookup(i => i.CustomerOrderId);

        return orders.Select(o => ToDto(o, items[o.Id])).ToList();
    }

    // ------------------------------------------------------------ ödeme

    public async Task<ServiceResult<CustomerOrderDto>> CheckoutAsync(Guid userId, CheckoutRequest req, CancellationToken ct = default)
    {
        static ServiceResult<CustomerOrderDto> Bad(string message, string code = "INVALID_REQUEST", int status = 400) =>
            ServiceResult<CustomerOrderDto>.Fail(status, code, message);

        var (address, addressError) = InputRules.ValidateAddress(req.Address);
        if (addressError is not null) return Bad(addressError);

        var cart = await GetCartAsync(userId, ct);
        if (cart.Lines.Count == 0) return Bad("Sepetiniz boş veya süresi dolmuş.", "EMPTY_CART");

        // --- ödeme yöntemi
        string pan, holder, cvv = CardHelper.Digits(req.Cvv);
        int expMonth, expYear;
        var isNewCard = false;

        if (req.SavedCardId is { } savedId)
        {
            await using var conn = await _db.OpenAsync(ct);
            var saved = await conn.QuerySingleOrDefaultAsync<PaymentCardRow>(new CommandDefinition(
                "SELECT * FROM payment_cards WHERE id = @savedId AND user_id = @userId", new { savedId, userId }, cancellationToken: ct));
            if (saved is null) return Bad("Kayıtlı kart bulunamadı.", "CARD_NOT_FOUND", 404);

            pan = _crypto.Decrypt(saved.CardNumberEnc, "card.number");
            holder = _crypto.Decrypt(saved.HolderNameEnc, "card.holder");
            expMonth = saved.ExpMonth;
            expYear = saved.ExpYear;
        }
        else if (req.Card is { } card)
        {
            isNewCard = true;
            pan = CardHelper.Digits(card.Number);
            holder = card.HolderName?.Trim() ?? "";
            expMonth = card.ExpMonth;
            expYear = card.ExpYear < 100 ? 2000 + card.ExpYear : card.ExpYear;
            cvv = CardHelper.Digits(card.Cvv);

            if (pan.Length is < 13 or > 19 || !CardHelper.PassesLuhn(pan)) return Bad("Kart numarası geçersiz.");
            if (holder.Length is < 2 or > 100) return Bad("Kart üzerindeki ad geçersiz.");
        }
        else
        {
            return Bad("Ödeme yöntemi seçiniz.");
        }

        if (expMonth is < 1 or > 12) return Bad("Son kullanma ayı geçersiz.");
        var nowUtc = DateTime.UtcNow;
        if (expYear < nowUtc.Year || (expYear == nowUtc.Year && expMonth < nowUtc.Month))
            return Bad("Kartın son kullanma tarihi geçmiş.");
        if (cvv.Length is < 3 or > 4) return Bad("CVV geçersiz.");

        // --- tahsilat
        var payment = await _gateway.AuthorizeAsync(new PaymentRequest(cart.Total, pan, holder, expMonth, expYear, cvv), ct);
        if (!payment.Approved)
            return Bad(payment.DeclineReason ?? "Ödeme reddedildi.", "PAYMENT_DECLINED", 402);

        var brand = CardHelper.Brand(pan);
        var last4 = pan[^4..];
        var header = new CheckoutHeader(
            Guid.NewGuid(),
            $"FS-{DateTime.UtcNow:yyMMdd}-{RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 6)}",
            _crypto.Encrypt(JsonSerializer.Serialize(address), "order.address"),
            brand, last4, payment.Reference);

        CheckoutResult result;
        try
        {
            result = await _inventory.CheckoutAsync(userId.ToString(), header, cart.Total, ct);
        }
        catch
        {
            await _gateway.VoidAsync(payment.Reference, CancellationToken.None);
            throw;
        }

        foreach (var part in result.Parts)
            await _notifier.PublishAsync(part, ct: CancellationToken.None);

        if (result.Status != CheckoutStatus.Ok)
        {
            await _gateway.VoidAsync(payment.Reference, CancellationToken.None);
            return result.Status switch
            {
                CheckoutStatus.Expired => Bad(result.Message + " Ödemeniz alınmadı; lütfen sepetinizi kontrol edin.", "CART_EXPIRED", 410),
                CheckoutStatus.EmptyCart => Bad(result.Message, "EMPTY_CART"),
                _ => Bad("Sepet tutarı değişti. Ödemeniz alınmadı; lütfen tekrar deneyin.", "CART_CHANGED", 409)
            };
        }

        // Sipariş tamamlandı; aşağıdakiler başarısız olsa bile sipariş geçerlidir.
        try
        {
            if (isNewCard && req.SaveCard) await SaveCardAsync(userId, pan, holder, expMonth, expYear, brand, last4, ct);
            if (req.SaveAddress) await _auth.SaveDefaultAddressAsync(userId, address!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sipariş sonrası kart/adres kaydı başarısız oldu.");
        }

        var items = result.Parts.Where(p => p.Order is not null).Select(p => new OrderItemRow
        {
            CustomerOrderId = result.Order!.Id,
            ProductId = p.Order!.ProductId,
            ProductName = p.Reservation!.ProductName,
            Barcode = p.Reservation.Barcode,
            Quantity = p.Order.Quantity,
            LineTotal = p.Order.TotalPrice
        });
        return ServiceResult<CustomerOrderDto>.Success(ToDto(result.Order!, items), 201);
    }

    // ------------------------------------------------------------ yardımcılar

    private async Task SaveCardAsync(
        Guid userId, string pan, string holder, int expMonth, int expYear, string brand, string last4, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(
                @"INSERT INTO payment_cards
                    (id, user_id, brand, last4, holder_name_enc, card_number_enc, fingerprint, exp_month, exp_year, created_at)
                  VALUES (@Id, @userId, @brand, @last4, @holderEnc, @panEnc, @fingerprint, @expMonth, @expYear, @now)",
                new
                {
                    Id = Guid.NewGuid(), userId, brand, last4,
                    holderEnc = _crypto.Encrypt(holder, "card.holder"),
                    panEnc = _crypto.Encrypt(pan, "card.number"),
                    fingerprint = _crypto.Fingerprint(pan),
                    expMonth, expYear, now = DateTime.UtcNow
                });
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // Aynı kart zaten kayıtlı.
        }
    }

    private CustomerOrderDto ToDto(CustomerOrderRow o, IEnumerable<OrderItemRow> items)
    {
        ShippingAddressDto? address = null;
        try { address = JsonSerializer.Deserialize<ShippingAddressDto>(_crypto.Decrypt(o.ShippingAddressEnc, "order.address")); }
        catch (Exception ex) { _logger.LogError(ex, "Sipariş adresi çözülemedi: {OrderId}", o.Id); }

        return new CustomerOrderDto(
            o.Id, o.OrderNo, o.TotalPrice, o.Status, o.PaymentBrand, o.PaymentLast4, address, o.CreatedAt,
            items.Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.Barcode, i.Quantity, i.LineTotal)).ToList());
    }

    private static int StatusOf(OpStatus s) => s switch
    {
        OpStatus.Invalid => 400,
        OpStatus.ProductNotFound or OpStatus.ReservationNotFound => 404,
        OpStatus.InsufficientStock or OpStatus.NotActive => 409,
        OpStatus.Expired => 410,
        _ => 500
    };

    private static string CodeOf(OpStatus s) => s switch
    {
        OpStatus.Invalid => "INVALID_REQUEST",
        OpStatus.ProductNotFound => "PRODUCT_NOT_FOUND",
        OpStatus.ReservationNotFound => "RESERVATION_NOT_FOUND",
        OpStatus.InsufficientStock => "INSUFFICIENT_STOCK",
        OpStatus.NotActive => "RESERVATION_NOT_ACTIVE",
        OpStatus.Expired => "RESERVATION_EXPIRED",
        _ => "UNKNOWN"
    };
}
