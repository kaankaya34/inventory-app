using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace StockEngine.Api.Security;

public class JwtOptions
{
    public string Issuer { get; set; } = "stock-engine";
    public string Audience { get; set; } = "stock-engine-clients";
    public int ExpiryMinutes { get; set; } = 480;
    // Yetkili (personel/yönetici) oturumları daha kısa tutulur.
    public int StaffExpiryMinutes { get; set; } = 120;
}

public class JwtTokenService
{
    private readonly SymmetricSecurityKey _key;
    private readonly JwtOptions _options;

    public JwtTokenService(byte[] signingKey, JwtOptions options)
    {
        _key = new SymmetricSecurityKey(signingKey);
        _options = options;
    }

    public SymmetricSecurityKey Key => _key;
    public JwtOptions Options => _options;

    // Token yalnızca kimliği taşır. Rol ve yetkiler token'a güvenilmeden her istekte veritabanından
    // (users + staff_users) alınır; token'daki "role" yalnızca bilgi amaçlıdır.
    public (string Token, DateTime ExpiresAt) Create(Guid id, string email, string fullName, string role)
    {
        var minutes = role == Models.Roles.Customer ? _options.ExpiryMinutes : _options.StaffExpiryMinutes;
        var expires = DateTime.UtcNow.AddMinutes(minutes);
        var claims = new[]
        {
            new Claim("sub", id.ToString()),
            new Claim("email", email),
            new Claim("name", fullName),
            new Claim("role", role),
            new Claim("jti", Guid.NewGuid().ToString("N"))
        };
        var token = new JwtSecurityToken(
            _options.Issuer, _options.Audience, claims,
            notBefore: DateTime.UtcNow, expires: expires,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
