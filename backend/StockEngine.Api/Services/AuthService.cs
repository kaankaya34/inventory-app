using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using StockEngine.Api.Models;
using StockEngine.Api.Security;

namespace StockEngine.Api.Services;

public static partial class InputRules
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[+0-9 ()\-]{7,20}$")]
    private static partial Regex PhoneRegex();

    public static bool IsEmail(string s) => s.Length <= 190 && EmailRegex().IsMatch(s);
    public static bool IsPhone(string s) => PhoneRegex().IsMatch(s);

    public static string? PasswordError(string? p)
    {
        if (string.IsNullOrEmpty(p) || p.Length < 8) return "Parola en az 8 karakter olmalıdır.";
        if (p.Length > 128) return "Parola en fazla 128 karakter olabilir.";
        if (!p.Any(char.IsLetter) || !p.Any(char.IsDigit)) return "Parola en az bir harf ve bir rakam içermelidir.";
        return null;
    }

    // Adresi doğrular ve kırpılmış kopyasını döndürür.
    public static (ShippingAddressDto? Address, string? Error) ValidateAddress(ShippingAddressDto? a)
    {
        if (a is null) return (null, "Teslimat adresi gereklidir.");
        var name = a.FullName?.Trim() ?? "";
        var phone = a.Phone?.Trim() ?? "";
        var city = a.City?.Trim() ?? "";
        var district = a.District?.Trim() ?? "";
        var line = a.AddressLine?.Trim() ?? "";
        var zip = a.PostalCode?.Trim() ?? "";

        if (name.Length is < 2 or > 100) return (null, "Ad soyad 2-100 karakter olmalıdır.");
        if (!IsPhone(phone)) return (null, "Geçerli bir telefon numarası giriniz.");
        if (city.Length is < 2 or > 60) return (null, "İl bilgisi gereklidir.");
        if (district.Length is < 2 or > 60) return (null, "İlçe bilgisi gereklidir.");
        if (line.Length is < 5 or > 250) return (null, "Açık adres 5-250 karakter olmalıdır.");
        if (zip.Length > 0 && (zip.Length is < 4 or > 10 || !zip.All(char.IsDigit))) return (null, "Posta kodu geçersiz.");
        return (new ShippingAddressDto(name, phone, city, district, line, zip), null);
    }
}

public class AuthService
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    // Kullanıcı bulunamasa da parola doğrulama süresi harcansın (zamanlama farkıyla hesap keşfini zorlaştırır).
    private static readonly string DummyHash = PasswordHasher.Hash("dummy-password-1");

    private readonly Database _db;
    private readonly IEncryptionService _crypto;
    private readonly JwtTokenService _jwt;
    private readonly UserAccessService _access;
    private readonly ILogger<AuthService> _logger;

    public AuthService(Database db, IEncryptionService crypto, JwtTokenService jwt, UserAccessService access, ILogger<AuthService> logger)
    {
        _db = db;
        _crypto = crypto;
        _jwt = jwt;
        _access = access;
        _logger = logger;
    }

    public async Task<ServiceResult<AuthResponse>> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        var fullName = req.FullName?.Trim() ?? "";
        var phone = req.Phone?.Trim();

        if (!InputRules.IsEmail(email))
            return ServiceResult<AuthResponse>.Fail(400, "INVALID_REQUEST", "Geçerli bir e-posta adresi giriniz.");
        if (InputRules.PasswordError(req.Password) is { } pwError)
            return ServiceResult<AuthResponse>.Fail(400, "INVALID_REQUEST", pwError);
        if (fullName.Length is < 2 or > 100)
            return ServiceResult<AuthResponse>.Fail(400, "INVALID_REQUEST", "Ad soyad 2-100 karakter olmalıdır.");
        if (!string.IsNullOrEmpty(phone) && !InputRules.IsPhone(phone))
            return ServiceResult<AuthResponse>.Fail(400, "INVALID_REQUEST", "Geçerli bir telefon numarası giriniz.");

        var user = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = PasswordHasher.Hash(req.Password!),
            FullName = fullName,
            PhoneEnc = string.IsNullOrEmpty(phone) ? null : _crypto.Encrypt(phone, "user.phone"),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(
                @"INSERT INTO users (id, email, password_hash, full_name, phone_enc, created_at)
                  VALUES (@Id, @Email, @PasswordHash, @FullName, @PhoneEnc, @CreatedAt)", user);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return ServiceResult<AuthResponse>.Fail(409, "EMAIL_TAKEN", "Bu e-posta adresiyle zaten bir hesap var.");
        }

        return ServiceResult<AuthResponse>.Success(await ToAuthResponseAsync(user, ct), 201);
    }

    public async Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        const string invalid = "E-posta veya parola hatalı.";
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        var password = req.Password ?? "";

        await using var conn = await _db.OpenAsync(ct);
        var user = await conn.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT * FROM users WHERE email = @email", new { email });

        if (user is null)
        {
            PasswordHasher.Verify(password, DummyHash);
            return ServiceResult<AuthResponse>.Fail(401, "INVALID_CREDENTIALS", invalid);
        }

        if (user.LockedUntil is { } until && until > DateTime.UtcNow)
            return ServiceResult<AuthResponse>.Fail(423, "ACCOUNT_LOCKED",
                "Çok fazla hatalı deneme. Lütfen birkaç dakika sonra tekrar deneyin.");

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            var lockNow = user.FailedLoginCount + 1 >= MaxFailedLogins;
            await conn.ExecuteAsync(
                @"UPDATE users SET failed_login_count = @count, locked_until = @lockedUntil WHERE id = @id",
                new
                {
                    count = lockNow ? 0 : user.FailedLoginCount + 1,
                    lockedUntil = lockNow ? DateTime.UtcNow.Add(LockDuration) : (DateTime?)null,
                    id = user.Id
                });
            if (lockNow) _logger.LogWarning("Hesap geçici olarak kilitlendi: {UserId}", user.Id);
            return ServiceResult<AuthResponse>.Fail(401, "INVALID_CREDENTIALS", invalid);
        }

        await conn.ExecuteAsync(
            "UPDATE users SET failed_login_count = 0, locked_until = NULL, last_login_at = @now WHERE id = @id",
            new { now = DateTime.UtcNow, id = user.Id });

        // Parola doğru ama hesap yönetici tarafından devre dışı bırakılmış.
        if (!user.IsActive)
            return ServiceResult<AuthResponse>.Fail(403, "ACCOUNT_DISABLED", "Hesabınız devre dışı bırakılmış. Lütfen yönetici ile iletişime geçin.");

        return ServiceResult<AuthResponse>.Success(await ToAuthResponseAsync(user, ct));
    }

    public async Task<UserRow?> GetUserAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserRow>("SELECT * FROM users WHERE id = @id", new { id });
    }

    public async Task<ServiceResult<ProfileDto>> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await GetUserAsync(userId, ct);
        return user is null
            ? ServiceResult<ProfileDto>.Fail(404, "USER_NOT_FOUND", "Kullanıcı bulunamadı.")
            : ServiceResult<ProfileDto>.Success(ToProfile(user));
    }

    public async Task<ServiceResult<ProfileDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest req, CancellationToken ct = default)
    {
        var fullName = req.FullName?.Trim() ?? "";
        var phone = req.Phone?.Trim();
        if (fullName.Length is < 2 or > 100)
            return ServiceResult<ProfileDto>.Fail(400, "INVALID_REQUEST", "Ad soyad 2-100 karakter olmalıdır.");
        if (!string.IsNullOrEmpty(phone) && !InputRules.IsPhone(phone))
            return ServiceResult<ProfileDto>.Fail(400, "INVALID_REQUEST", "Geçerli bir telefon numarası giriniz.");

        string? addressEnc = null;
        if (req.Address is not null && HasAnyValue(req.Address))
        {
            var (address, error) = InputRules.ValidateAddress(req.Address);
            if (error is not null) return ServiceResult<ProfileDto>.Fail(400, "INVALID_REQUEST", error);
            addressEnc = EncryptAddress(address!);
        }

        await using var conn = await _db.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(
            "UPDATE users SET full_name = @fullName, phone_enc = @phoneEnc, address_enc = @addressEnc WHERE id = @userId",
            new
            {
                fullName,
                phoneEnc = string.IsNullOrEmpty(phone) ? null : _crypto.Encrypt(phone, "user.phone"),
                addressEnc,
                userId
            });
        if (affected == 0) return ServiceResult<ProfileDto>.Fail(404, "USER_NOT_FOUND", "Kullanıcı bulunamadı.");

        return await GetProfileAsync(userId, ct);
    }

    public async Task SaveDefaultAddressAsync(Guid userId, ShippingAddressDto address, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync("UPDATE users SET address_enc = @enc WHERE id = @userId",
            new { enc = EncryptAddress(address), userId });
    }

    // ------------------------------------------------------------ yardımcılar

    public string EncryptAddress(ShippingAddressDto address) =>
        _crypto.Encrypt(JsonSerializer.Serialize(address), "user.address");

    public ShippingAddressDto? DecryptAddress(string? enc, string purpose = "user.address")
    {
        if (string.IsNullOrEmpty(enc)) return null;
        try { return JsonSerializer.Deserialize<ShippingAddressDto>(_crypto.Decrypt(enc, purpose)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adres çözülemedi.");
            return null;
        }
    }

    private ProfileDto ToProfile(UserRow u) => new(
        u.Email, u.FullName,
        string.IsNullOrEmpty(u.PhoneEnc) ? null : _crypto.Decrypt(u.PhoneEnc, "user.phone"),
        DecryptAddress(u.AddressEnc), Roles.Customer);

    private async Task<AuthResponse> ToAuthResponseAsync(UserRow user, CancellationToken ct)
    {
        var dto = await ToUserDtoAsync(user, ct);
        var (token, expires) = _jwt.Create(dto.Id, dto.Email, dto.FullName, dto.Role);
        return new AuthResponse(token, expires, dto);
    }

    // Kişinin güncel rolü ve yetkileri (yetki kaydı varsa personel/yönetici, yoksa müşteri).
    public async Task<UserDto> ToUserDtoAsync(UserRow user, CancellationToken ct = default)
    {
        _access.Invalidate(user.Id);
        var access = await _access.GetAsync(user.Id, ct);
        return new UserDto(user.Id, user.Email, user.FullName,
            access?.Role ?? Roles.Customer, access?.Permissions ?? Array.Empty<string>());
    }

    private static bool HasAnyValue(ShippingAddressDto a) =>
        !string.IsNullOrWhiteSpace(a.FullName) || !string.IsNullOrWhiteSpace(a.City) ||
        !string.IsNullOrWhiteSpace(a.AddressLine) || !string.IsNullOrWhiteSpace(a.District);
}
