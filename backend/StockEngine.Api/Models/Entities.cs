namespace StockEngine.Api.Models;

public static class ReservationStatus
{
    public const string Active = "ACTIVE";
    public const string Confirmed = "CONFIRMED";
    public const string Expired = "EXPIRED";
    public const string Cancelled = "CANCELLED";
}

public static class StockAction
{
    public const string Reserve = "RESERVE";
    public const string ConfirmOrder = "CONFIRM_ORDER";
    public const string ExpireRollback = "EXPIRE_ROLLBACK";
    public const string Cancel = "CANCEL";
    public const string ManualRestock = "MANUAL_RESTOCK";
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Barcode { get; set; } = "";
    public decimal Price { get; set; }
    public int PhysicalStock { get; set; }
    public int ReservedStock { get; set; }
    public int AvailableStock { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ReservationRow
{
    public Guid Id { get; set; }
    public int ProductId { get; set; }
    public string Barcode { get; set; } = "";
    public string SessionOrUserId { get; set; } = "";
    public int Quantity { get; set; }
    public string Status { get; set; } = ReservationStatus.Active;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ProductName { get; set; } = "";
}

public class OrderRow
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public int ProductId { get; set; }
    public string UserId { get; set; } = "";
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CustomerOrderId { get; set; }
}

public class StockLog
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public string Barcode { get; set; } = "";
    public string ActionType { get; set; } = "";
    public int QuantityChanged { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
