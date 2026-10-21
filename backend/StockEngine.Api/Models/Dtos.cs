namespace StockEngine.Api.Models;

public enum OpStatus
{
    Ok,
    Invalid,
    ProductNotFound,
    ReservationNotFound,
    InsufficientStock,
    NotActive,
    Expired
}

// Bir stok operasyonunun sonucu. Başarısız olsa bile (örn. Expired) yan etkisi olan
// değişiklikleri (ürün/rezervasyon/log) taşır; bildirim katmanı bunları yayınlar.
public sealed class OpResult
{
    public OpStatus Status { get; init; }
    public string Message { get; init; } = "";
    public Product? Product { get; init; }
    public ReservationRow? Reservation { get; init; }
    public OrderRow? Order { get; init; }
    public List<StockLog> Logs { get; init; } = new();
    public int? AvailableStock { get; init; }
    public bool IsOk => Status == OpStatus.Ok;

    public static OpResult Fail(OpStatus status, string message, int? availableStock = null) =>
        new() { Status = status, Message = message, AvailableStock = availableStock };
}

public record ProductDto(
    int Id, string Name, string Sku, string Barcode, decimal Price,
    int PhysicalStock, int ReservedStock, int AvailableStock, int Version, DateTime UpdatedAt);

public record ReservationDto(
    Guid Id, int ProductId, string ProductName, string Barcode, string UserId, int Quantity,
    string Status, DateTime ExpiresAt, DateTime CreatedAt, int RemainingSeconds);

public record OrderDto(
    Guid Id, Guid ReservationId, int ProductId, string UserId, int Quantity, decimal TotalPrice, DateTime CreatedAt);

public record ReserveResponse(
    Guid ReservationId, string Barcode, string ProductName, int Quantity, string Status,
    DateTime ExpiresAt, int RemainingSeconds, int AvailableStock);

public record ConfirmResponse(OrderDto Order, ReservationDto Reservation, ProductDto Product);

public record CancelResponse(ReservationDto Reservation, ProductDto Product);

public record RestockResponse(ProductDto Product, int AddedQuantity);

public record ApiError(bool Success, string Code, string Message, int? AvailableStock = null);

public record ReserveRequest(string? Barcode, int Quantity, string? UserId);
public record ConfirmRequest(Guid ReservationId);
public record CancelRequest(Guid ReservationId);
public record RestockRequest(string? Barcode, int AddQuantity);
public record SimulationRequest(string? Barcode, int? RequestCount, int? Quantity);

public record SimulationItem(int Index, string UserId, bool Success, string Status, double DurationMs, Guid? ReservationId);

public record SimulationReport(
    string Barcode, string ProductName, int RequestCount, int QuantityPerRequest,
    int SuccessCount, int FailedCount, int ErrorCount,
    int InitialAvailableStock, int FinalAvailableStock, int FinalReservedStock,
    bool OversellingPrevented, bool StockConsistent,
    double TotalElapsedMs, double MinMs, double AvgMs, double P95Ms, double MaxMs,
    IReadOnlyList<SimulationItem> Results);

public static class Mapper
{
    public static ProductDto ToDto(this Product p) => new(
        p.Id, p.Name, p.Sku, p.Barcode, p.Price, p.PhysicalStock, p.ReservedStock,
        p.AvailableStock, p.Version, p.UpdatedAt);

    // RemainingSeconds istemci/sunucu saat farkından etkilenmeden geri sayım yapılabilsin diye sunucuda hesaplanır.
    public static ReservationDto ToDto(this ReservationRow r) => new(
        r.Id, r.ProductId, r.ProductName, r.Barcode, r.SessionOrUserId, r.Quantity, r.Status,
        r.ExpiresAt, r.CreatedAt,
        r.Status == ReservationStatus.Active
            ? Math.Max(0, (int)Math.Ceiling((r.ExpiresAt - DateTime.UtcNow).TotalSeconds))
            : 0);

    public static OrderDto ToDto(this OrderRow o) => new(
        o.Id, o.ReservationId, o.ProductId, o.UserId, o.Quantity, o.TotalPrice, o.CreatedAt);
}
