using StockEngine.Api.Models;

namespace StockEngine.Api.Services;

public class ReservationOptions
{
    public int TtlMinutes { get; set; } = 5;
    public int CleanupIntervalSeconds { get; set; } = 1;
}

// Stok motorunun sözleşmesi. Şu an MySQL atomik implementasyonu (MySqlInventoryService) kullanılır;
// Redis (Lua script) tabanlı bir implementasyon aynı arayüzle takılabilir.
public interface IInventoryService
{
    Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct = default);
    Task<Product?> GetProductByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<IReadOnlyList<ReservationRow>> GetReservationsAsync(int limit, CancellationToken ct = default);
    Task<IReadOnlyList<StockLog>> GetLogsAsync(int limit, CancellationToken ct = default);

    Task<OpResult> ReserveAsync(string? barcode, int quantity, string? userId, CancellationToken ct = default);
    Task<OpResult> ConfirmAsync(Guid reservationId, CancellationToken ct = default);

    // ownerUserId verilirse yalnızca o kullanıcıya ait rezervasyon iptal edilebilir.
    Task<OpResult> CancelAsync(Guid reservationId, string? ownerUserId = null, CancellationToken ct = default);

    // Kullanıcının sepetindeki (ACTIVE) tüm rezervasyonları tek transaction'da siparişe çevirir.
    Task<CheckoutResult> CheckoutAsync(string userId, CheckoutHeader header, decimal expectedTotal, CancellationToken ct = default);
    Task<OpResult> RestockAsync(string? barcode, int addQuantity, CancellationToken ct = default);

    // Süresi dolmuş ACTIVE rezervasyonları EXPIRED yapıp stoğu iade eder.
    Task<IReadOnlyList<OpResult>> ExpireDueReservationsAsync(CancellationToken ct = default);
}

// Başarılı/yan etkili operasyon sonuçlarını bağlı istemcilere (SignalR) yayınlar.
public interface IStockNotifier
{
    Task PublishAsync(OpResult result, bool includeProduct = true, CancellationToken ct = default);
    Task PublishProductAsync(Product product, CancellationToken ct = default);
}
