using System.Text.Json;
using Dapper;
using MySqlConnector;
using StockEngine.Api.Models;
using StockEngine.Api.Security;

namespace StockEngine.Api.Services;

// Yalnızca ADMIN: kullanıcı ve yetki yönetimi. Herkes `users` tablosunda tek hesaba sahiptir (müşteri);
// personel/yönetici yetkileri ayrı `staff_users` tablosunda (user_id ile bağlı) tutulur.
// Ayrıca tüm müşteri siparişlerini (orders.view yetkisiyle) listeler.
public class AdminService
{
    private static readonly string[] ValidRoles = { Roles.Customer, Roles.Staff, Roles.Admin };

    private readonly Database _db;
    private readonly IEncryptionService _crypto;
    private readonly UserAccessService _access;
    private readonly ILogger<AdminService> _logger;

    public AdminService(Database db, IEncryptionService crypto, UserAccessService access, ILogger<AdminService> logger)
    {
        _db = db;
        _crypto = crypto;
        _access = access;
        _logger = logger;
    }

    // ------------------------------------------------------------ kullanıcılar

    private const string UserSelect =
        @"SELECT u.id, u.email, u.full_name, u.is_active, u.created_at, u.last_login_at,
                 s.role AS staff_role, s.permissions AS staff_permissions
          FROM users u LEFT JOIN staff_users s ON s.user_id = u.id";

    public async Task<IReadOnlyList<AdminUserDto>> ListUsersAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<AdminUserRow>(new CommandDefinition(
            UserSelect + " ORDER BY CASE s.role WHEN 'ADMIN' THEN 0 WHEN 'STAFF' THEN 1 ELSE 2 END, u.created_at DESC LIMIT 1000",
            cancellationToken: ct));
        return rows.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<AdminUserDto>> CreateUserAsync(Guid actorId, CreateUserRequest req, CancellationToken ct = default)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        var fullName = req.FullName?.Trim() ?? "";

        if (!InputRules.IsEmail(email)) return Bad("Geçerli bir e-posta adresi giriniz.");
        if (InputRules.PasswordError(req.Password) is { } pwError) return Bad(pwError);
        if (fullName.Length is < 2 or > 100) return Bad("Ad soyad 2-100 karakter olmalıdır.");
        var (role, permissions, error) = Normalize(req.Role, req.Permissions);
        if (error is not null) return Bad(error);

        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        try
        {
            await _db.InTransactionAsync<bool>(async (conn, tx) =>
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO users (id, email, password_hash, full_name, created_at)
                      VALUES (@userId, @email, @hash, @fullName, @now)",
                    new { userId, email, hash = PasswordHasher.Hash(req.Password!), fullName, now }, tx);
                if (role != Roles.Customer)
                    await UpsertStaffAsync(conn, tx, userId, role, permissions, now, actorId);
                return true;
            }, ct);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return ServiceResult<AdminUserDto>.Fail(409, "EMAIL_TAKEN", "Bu e-posta adresiyle zaten bir hesap var. Yetki vermek için mevcut hesabı düzenleyin.");
        }

        _logger.LogWarning("YETKİ: {Actor} hesap oluşturdu: {Email} rol={Role} yetkiler=[{Perms}]", actorId, email, role, string.Join(',', permissions));
        return ServiceResult<AdminUserDto>.Success(
            new AdminUserDto(userId, email, fullName, role, Permissions.For(role, Permissions.Serialize(permissions)), true, now, null), 201);
    }

    public async Task<ServiceResult<AdminUserDto>> UpdateUserAsync(
        Guid actorId, Guid userId, UpdateUserRequest req, CancellationToken ct = default)
    {
        var result = await _db.InTransactionAsync<ServiceResult<AdminUserDto>>(async (conn, tx) =>
        {
            var user = await conn.QuerySingleOrDefaultAsync<AdminUserRow>(
                UserSelect + " WHERE u.id = @userId FOR UPDATE", new { userId }, tx);
            if (user is null) return ServiceResult<AdminUserDto>.Fail(404, "USER_NOT_FOUND", "Kullanıcı bulunamadı.");

            var fullName = req.FullName is null ? user.FullName : req.FullName.Trim();
            if (fullName.Length is < 2 or > 100) return Bad("Ad soyad 2-100 karakter olmalıdır.");

            var currentRole = user.StaffRole ?? Roles.Customer;
            var (role, permissions, error) = Normalize(req.Role ?? currentRole, req.Permissions ?? Permissions.Parse(user.StaffPermissions));
            if (error is not null) return Bad(error);
            var isActive = req.IsActive ?? user.IsActive;

            // Kendi hesabınızı kilitlemeyi/yetkisizleştirmeyi engelle.
            if (userId == actorId && (role != currentRole || !isActive))
                return ServiceResult<AdminUserDto>.Fail(409, "SELF_LOCKOUT", "Kendi rolünüzü veya aktifliğinizi değiştiremezsiniz.");

            // Sistemde en az bir aktif yönetici kalmalı.
            var losesAdmin = currentRole == Roles.Admin && user.IsActive && (role != Roles.Admin || !isActive);
            if (losesAdmin)
            {
                var others = await conn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM staff_users s JOIN users u ON u.id = s.user_id
                      WHERE s.role = 'ADMIN' AND u.is_active = 1 AND s.user_id <> @userId FOR UPDATE",
                    new { userId }, tx);
                if (others == 0)
                    return ServiceResult<AdminUserDto>.Fail(409, "LAST_ADMIN", "Sistemdeki son aktif yönetici değiştirilemez.");
            }

            await conn.ExecuteAsync(
                "UPDATE users SET full_name = @fullName, is_active = @isActive WHERE id = @userId",
                new { fullName, isActive, userId }, tx);

            // Müşteri: yetki kaydı silinir (kişi yine müşteri olarak kalır). Personel/yönetici: kayıt eklenir/güncellenir.
            if (role == Roles.Customer)
                await conn.ExecuteAsync("DELETE FROM staff_users WHERE user_id = @userId", new { userId }, tx);
            else
                await UpsertStaffAsync(conn, tx, userId, role, permissions, DateTime.UtcNow, actorId);

            return ServiceResult<AdminUserDto>.Success(new AdminUserDto(
                userId, user.Email, fullName, role, Permissions.For(role, Permissions.Serialize(permissions)),
                isActive, user.CreatedAt, user.LastLoginAt));
        }, ct);

        if (result.Ok)
        {
            _access.Invalidate(userId); // değişiklik anında geçerli olsun
            _logger.LogWarning("YETKİ: {Actor} kullanıcıyı güncelledi: {UserId} rol={Role} yetkiler=[{Perms}] aktif={Active}",
                actorId, userId, result.Value!.Role, string.Join(',', result.Value.Permissions), result.Value.IsActive);
        }
        return result;
    }

    public async Task<ServiceResult<bool>> ResetPasswordAsync(Guid actorId, Guid userId, string? newPassword, CancellationToken ct = default)
    {
        if (InputRules.PasswordError(newPassword) is { } pwError)
            return ServiceResult<bool>.Fail(400, "INVALID_REQUEST", pwError);

        await using var conn = await _db.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET password_hash = @hash, failed_login_count = 0, locked_until = NULL WHERE id = @userId",
            new { hash = PasswordHasher.Hash(newPassword!), userId }, cancellationToken: ct));
        if (affected == 0) return ServiceResult<bool>.Fail(404, "USER_NOT_FOUND", "Kullanıcı bulunamadı.");

        _logger.LogWarning("YETKİ: {Actor} kullanıcının parolasını sıfırladı: {UserId}", actorId, userId);
        return ServiceResult<bool>.Success(true);
    }

    // İlk kurulumda hiç yönetici yoksa oluşturur. Aynı e-postalı bir kullanıcı varsa onu yönetici yapar (parolası değişmez);
    // yoksa yeni kullanıcı açar. Parola verilmezse rastgele üretilir ve App_Data'ya yazılır.
    public async Task EnsureAdminAsync(IConfiguration config, string contentRoot, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM staff_users WHERE role = 'ADMIN'");
        if (exists > 0) return;

        var email = (config["Admin:Email"] ?? "admin@shop.local").Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        var existingId = await conn.ExecuteScalarAsync<Guid?>("SELECT id FROM users WHERE email = @email", new { email });

        if (existingId is { } id)
        {
            await conn.ExecuteAsync(
                "INSERT INTO staff_users (user_id, role, permissions, created_at) VALUES (@id, 'ADMIN', NULL, @now)", new { id, now });
            await conn.ExecuteAsync("UPDATE users SET is_active = 1 WHERE id = @id", new { id });
            _logger.LogWarning("Mevcut kullanıcı yönetici yapıldı: {Email} (parolası değişmedi).", email);
            return;
        }

        var password = config["Admin:Password"];
        var generated = string.IsNullOrWhiteSpace(password);
        if (generated)
            password = System.Security.Cryptography.RandomNumberGenerator.GetString(
                "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789", 16) + "9a";

        var userId = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO users (id, email, password_hash, full_name, created_at) VALUES (@userId, @email, @hash, 'Yönetici', @now)",
            new { userId, email, hash = PasswordHasher.Hash(password!), now });
        await conn.ExecuteAsync(
            "INSERT INTO staff_users (user_id, role, permissions, created_at) VALUES (@userId, 'ADMIN', NULL, @now)", new { userId, now });

        if (generated)
        {
            var dir = Path.Combine(contentRoot, "App_Data");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "admin-credentials.txt");
            await File.WriteAllTextAsync(path, $"E-posta: {email}{Environment.NewLine}Parola: {password}{Environment.NewLine}", ct);
            _logger.LogWarning("Yönetici hesabı oluşturuldu: {Email} — rastgele parola {Path} dosyasına yazıldı.", email, path);
        }
        else
        {
            _logger.LogWarning("Yönetici hesabı oluşturuldu: {Email} (parola yapılandırmadan).", email);
        }
    }

    // ------------------------------------------------------------ siparişler (orders.view)

    public async Task<IReadOnlyList<AdminOrderDto>> ListOrdersAsync(int limit, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var orders = (await conn.QueryAsync<AdminOrderRow>(new CommandDefinition(
            @"SELECT co.id, co.order_no, co.user_id, co.total_price, co.status, co.payment_brand, co.payment_last4,
                     co.payment_ref, co.shipping_address_enc, co.created_at,
                     u.email AS customer_email, u.full_name AS customer_name
              FROM customer_orders co JOIN users u ON u.id = co.user_id
              ORDER BY co.created_at DESC, co.id LIMIT @limit",
            new { limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct))).AsList();
        if (orders.Count == 0) return Array.Empty<AdminOrderDto>();

        var ids = orders.Select(o => o.Id).ToArray();
        var items = (await conn.QueryAsync<OrderItemRow>(new CommandDefinition(
            @"SELECT o.customer_order_id, o.product_id, p.name AS product_name, p.barcode,
                     o.quantity, o.total_price AS line_total
              FROM orders o JOIN products p ON p.id = o.product_id
              WHERE o.customer_order_id IN @ids ORDER BY o.created_at, o.id",
            new { ids }, cancellationToken: ct))).ToLookup(i => i.CustomerOrderId);

        return orders.Select(o =>
        {
            ShippingAddressDto? address = null;
            try { address = JsonSerializer.Deserialize<ShippingAddressDto>(_crypto.Decrypt(o.ShippingAddressEnc, "order.address")); }
            catch (Exception ex) { _logger.LogError(ex, "Sipariş adresi çözülemedi: {OrderId}", o.Id); }

            return new AdminOrderDto(
                o.Id, o.OrderNo, o.CustomerEmail, o.CustomerName, o.TotalPrice, o.Status, o.PaymentBrand, o.PaymentLast4,
                address?.City, address?.District, o.CreatedAt,
                items[o.Id].Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.Barcode, i.Quantity, i.LineTotal)).ToList());
        }).ToList();
    }

    // ------------------------------------------------------------ yardımcılar

    private static Task UpsertStaffAsync(
        MySqlConnection conn, MySqlTransaction tx, Guid userId, string role, string[] permissions, DateTime now, Guid actorId) =>
        conn.ExecuteAsync(
            @"INSERT INTO staff_users (user_id, role, permissions, created_at, created_by)
              VALUES (@userId, @role, @perms, @now, @actorId)
              ON DUPLICATE KEY UPDATE role = VALUES(role), permissions = VALUES(permissions)",
            new { userId, role, perms = Permissions.Serialize(permissions), now, actorId }, tx);

    // Rol geçerli mi; yetkiler yalnızca PERSONEL'e anlamlıdır (ADMIN zaten hepsine sahip, MÜŞTERİ hiçbirine).
    private static (string Role, string[] Permissions, string? Error) Normalize(string? role, string[]? permissions)
    {
        var r = role?.Trim().ToUpperInvariant() ?? "";
        if (!ValidRoles.Contains(r)) return ("", Array.Empty<string>(), "Rol CUSTOMER, STAFF veya ADMIN olmalıdır.");

        var requested = (permissions ?? Array.Empty<string>()).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToArray();
        var unknown = requested.Where(p => !Permissions.Assignable.Contains(p)).ToArray();
        if (unknown.Length > 0)
            return ("", Array.Empty<string>(), $"Geçersiz veya devredilemeyen yetki: {string.Join(", ", unknown)}");

        return (r, r == Roles.Staff ? requested : Array.Empty<string>(), null);
    }

    private static ServiceResult<AdminUserDto> Bad(string message) =>
        ServiceResult<AdminUserDto>.Fail(400, "INVALID_REQUEST", message);

    private static AdminUserDto ToDto(AdminUserRow u)
    {
        var role = u.StaffRole ?? Roles.Customer;
        return new AdminUserDto(u.Id, u.Email, u.FullName, role, Permissions.For(role, u.StaffPermissions),
            u.IsActive, u.CreatedAt, u.LastLoginAt);
    }
}
