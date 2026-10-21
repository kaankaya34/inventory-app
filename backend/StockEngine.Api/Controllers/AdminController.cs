using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockEngine.Api.Models;
using StockEngine.Api.Security;
using StockEngine.Api.Services;

namespace StockEngine.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ApiControllerBase
{
    private readonly AdminService _admin;

    public AdminController(AdminService admin) => _admin = admin;

    // ---- yetki kataloğu ve kullanıcı yönetimi: yalnızca ADMIN (devredilemez)
    [Authorize(Policy = Permissions.UsersManage)]
    [HttpGet("permissions")]
    public IReadOnlyList<PermissionInfo> GetPermissions() => Permissions.Catalog;

    [Authorize(Policy = Permissions.UsersManage)]
    [HttpGet("users")]
    public async Task<IReadOnlyList<AdminUserDto>> ListUsers(CancellationToken ct) => await _admin.ListUsersAsync(ct);

    [Authorize(Policy = Permissions.UsersManage)]
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct) =>
        From(await _admin.CreateUserAsync(CurrentUserId, request, ct));

    [Authorize(Policy = Permissions.UsersManage)]
    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct) =>
        From(await _admin.UpdateUserAsync(CurrentUserId, id, request, ct));

    [Authorize(Policy = Permissions.UsersManage)]
    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await _admin.ResetPasswordAsync(CurrentUserId, id, request.NewPassword, ct);
        return result.Ok ? NoContent() : From(result);
    }

    // ---- tüm müşteri siparişleri
    [Authorize(Policy = Permissions.OrdersView)]
    [HttpGet("orders")]
    public async Task<IReadOnlyList<AdminOrderDto>> ListOrders([FromQuery] int limit = 100, CancellationToken ct = default) =>
        await _admin.ListOrdersAsync(limit, ct);
}
