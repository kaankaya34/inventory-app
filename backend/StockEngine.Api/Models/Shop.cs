namespace StockEngine.Api.Models;

public static class Roles
{
    public const string Customer = "CUSTOMER";
    public const string Staff = "STAFF";
    public const string Admin = "ADMIN";
}

// ------------------------------------------------------------------ DB satırları

// users tablosu: HERKES (müşteri, personel, yönetici). Tek e-posta, tek parola.
// Personel/yönetici yetkileri ayrı `staff_users` tablosunda tutulur (aşağıdaki StaffRow).
public class UserRow
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? PhoneEnc { get; set; }
    public string? AddressEnc { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

// staff_users tablosu: yetki kaydı. Kaydı olan kişi (user_id) personel/yönetici olur; kaydı silinen yine müşteri olarak kalır.
public class StaffRow
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = Roles.Staff;
    public string? Permissions { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public class PaymentCardRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Brand { get; set; } = "";
    public string Last4 { get; set; } = "";
    public string HolderNameEnc { get; set; } = "";
    public string CardNumberEnc { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CustomerOrderRow
{
    public Guid Id { get; set; }
    public string OrderNo { get; set; } = "";
    public Guid UserId { get; set; }
    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = "PAID";
    public string PaymentBrand { get; set; } = "";
    public string PaymentLast4 { get; set; } = "";
    public string PaymentRef { get; set; } = "";
    public string ShippingAddressEnc { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class OrderItemRow
{
    public Guid CustomerOrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Barcode { get; set; } = "";
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class CartRow
{
    public Guid ReservationId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Sku { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// ------------------------------------------------------------------ checkout (servis katmanı)

public record CheckoutHeader(
    Guid Id, string OrderNo, string ShippingAddressEnc, string PaymentBrand, string PaymentLast4, string PaymentRef);

public enum CheckoutStatus { Ok, EmptyCart, Expired, TotalMismatch }

public sealed class CheckoutResult
{
    public CheckoutStatus Status { get; init; }
    public string Message { get; init; } = "";
    public CustomerOrderRow? Order { get; init; }
    // Yayınlanacak yan etkiler (ürün/rezervasyon/log).
    public List<OpResult> Parts { get; init; } = new();

    public static CheckoutResult Fail(CheckoutStatus status, string message, List<OpResult>? parts = null) =>
        new() { Status = status, Message = message, Parts = parts ?? new() };

    public static CheckoutResult Success(CustomerOrderRow order, List<OpResult> parts) =>
        new() { Status = CheckoutStatus.Ok, Order = order, Parts = parts };
}

// HTTP durum kodu taşıyan genel servis sonucu.
public sealed class ServiceResult<T>
{
    public T? Value { get; init; }
    public int Status { get; init; } = 200;
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public bool Ok => Status is >= 200 and < 300;

    public static ServiceResult<T> Success(T value, int status = 200) => new() { Value = value, Status = status };
    public static ServiceResult<T> Fail(int status, string code, string message) =>
        new() { Status = status, Code = code, Message = message };
}

// ------------------------------------------------------------------ DTO'lar

public record RegisterRequest(string? Email, string? Password, string? FullName, string? Phone);
public record LoginRequest(string? Email, string? Password);
public record UserDto(Guid Id, string Email, string FullName, string Role, IReadOnlyList<string> Permissions);
public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

public record ShippingAddressDto(
    string? FullName, string? Phone, string? City, string? District, string? AddressLine, string? PostalCode);

public record ProfileDto(string Email, string FullName, string? Phone, ShippingAddressDto? Address, string Role);
public record UpdateProfileRequest(string? FullName, string? Phone, ShippingAddressDto? Address);

public record AddToCartRequest(string? Barcode, int Quantity);

public record CartLineDto(
    Guid ReservationId, int ProductId, string ProductName, string Barcode, string Sku,
    decimal UnitPrice, int Quantity, decimal LineTotal, DateTime ExpiresAt, int RemainingSeconds);

public record CartDto(IReadOnlyList<CartLineDto> Lines, decimal Total, int ItemCount);

// CVV hiçbir zaman saklanmaz; yalnızca ödeme anında gateway'e iletilir.
public record CardInput(string? Number, string? HolderName, int ExpMonth, int ExpYear, string? Cvv);

public record CheckoutRequest(
    ShippingAddressDto? Address, bool SaveAddress,
    Guid? SavedCardId, CardInput? Card, bool SaveCard, string? Cvv);

public record SavedCardDto(Guid Id, string Brand, string Last4, string HolderName, int ExpMonth, int ExpYear);

public record OrderItemDto(int ProductId, string ProductName, string Barcode, int Quantity, decimal LineTotal);

public record CustomerOrderDto(
    Guid Id, string OrderNo, decimal TotalPrice, string Status, string PaymentBrand, string PaymentLast4,
    ShippingAddressDto? ShippingAddress, DateTime CreatedAt, IReadOnlyList<OrderItemDto> Items);
