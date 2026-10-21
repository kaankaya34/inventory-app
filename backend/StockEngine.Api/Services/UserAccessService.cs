using Dapper;
using Microsoft.Extensions.Caching.Memory;
using StockEngine.Api.Models;
using StockEngine.Api.Security;

namespace StockEngine.Api.Services;

// Rol: yetki kaydı (staff_users) varsa ADMIN/STAFF, yoksa CUSTOMER. Bir kişi her zaman aynı zamanda müşteridir.
public record AccessInfo(string Role, string[] Permissions, bool IsActive);

// Her istekte rol/yetki/aktiflik bilgisi (JWT'de değil) veritabanından gelir; böylece yetki değişiklikleri
// ve hesap devre dışı bırakma, token süresi dolmasını beklemeden hemen geçerli olur. Kısa süreli önbellek kullanılır.
public class UserAccessService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly Database _db;
    private readonly IMemoryCache _cache;

    public UserAccessService(Database db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<AccessInfo?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(Key(userId), out AccessInfo? cached)) return cached;

        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<AdminUserRow>(new CommandDefinition(
            @"SELECT u.id, u.is_active, s.role AS staff_role, s.permissions AS staff_permissions
              FROM users u LEFT JOIN staff_users s ON s.user_id = u.id
              WHERE u.id = @userId", new { userId }, cancellationToken: ct));

        AccessInfo? info = null;
        if (row is not null)
        {
            var role = row.StaffRole ?? Roles.Customer;
            info = new AccessInfo(role, Permissions.For(role, row.StaffPermissions), row.IsActive);
        }

        _cache.Set(Key(userId), info, Ttl);
        return info;
    }

    public void Invalidate(Guid userId) => _cache.Remove(Key(userId));

    private static string Key(Guid id) => $"access:{id}";
}
