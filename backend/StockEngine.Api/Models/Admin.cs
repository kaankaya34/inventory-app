namespace StockEngine.Api.Models;

// Yönetim ekranında bir kişi: tek hesap (users) + varsa yetki kaydı (staff_users).
public class AdminUserRow : UserRow
{
    public string? StaffRole { get; set; }
    public string? StaffPermissions { get; set; }
}

public record AdminUserDto(
    Guid Id, string Email, string FullName, string Role, IReadOnlyList<string> Permissions,
    bool IsActive, DateTime CreatedAt, DateTime? LastLoginAt);

public record CreateUserRequest(string? Email, string? Password, string? FullName, string? Role, string[]? Permissions);
public record UpdateUserRequest(string? FullName, string? Role, string[]? Permissions, bool? IsActive);
public record ResetPasswordRequest(string? NewPassword);

public class AdminOrderRow : CustomerOrderRow
{
    public string CustomerEmail { get; set; } = "";
    public string CustomerName { get; set; } = "";
}

// Veri minimizasyonu: yönetici listesinde tam adres/telefon yok, yalnızca il/ilçe.
public record AdminOrderDto(
    Guid Id, string OrderNo, string CustomerEmail, string CustomerName, decimal TotalPrice, string Status,
    string PaymentBrand, string PaymentLast4, string? City, string? District, DateTime CreatedAt,
    IReadOnlyList<OrderItemDto> Items);
