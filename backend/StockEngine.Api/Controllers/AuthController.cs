using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StockEngine.Api.Models;
using StockEngine.Api.Services;

namespace StockEngine.Api.Controllers;

[ApiController]
[EnableRateLimiting("auth")]
public class AuthController : ApiControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth) => _auth = auth;

    // Herkes (müşteri, personel, yönetici) tek hesapla, tek girişle oturum açar; rol ve yetkiler yanıtta gelir.
    // Kayıt olan herkes müşteri olur; personel/yönetici yetkisini yalnızca ADMIN verir.
    [HttpPost("api/auth/register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct) =>
        From(await _auth.RegisterAsync(request, ct));

    [HttpPost("api/auth/login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        From(await _auth.LoginAsync(request, ct));

    [Authorize]
    [HttpGet("api/auth/me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await _auth.GetUserAsync(CurrentUserId, ct);
        return user is null ? Unauthorized() : Ok(await _auth.ToUserDtoAsync(user, ct));
    }
}
