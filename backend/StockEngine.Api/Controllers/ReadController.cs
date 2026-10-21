using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockEngine.Api.Models;
using StockEngine.Api.Security;
using StockEngine.Api.Services;

namespace StockEngine.Api.Controllers;

[ApiController]
public class ReadController : ApiControllerBase
{
    private readonly IInventoryService _inventory;

    public ReadController(IInventoryService inventory) => _inventory = inventory;

    [Authorize(Policy = Permissions.StockView)]
    [HttpGet("api/reservations")]
    public async Task<ActionResult<IEnumerable<ReservationDto>>> Reservations([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var rows = await _inventory.GetReservationsAsync(limit, ct);
        return Ok(rows.Select(r => r.ToDto()));
    }

    [Authorize(Policy = Permissions.StockView)]
    [HttpGet("api/logs")]
    public async Task<ActionResult<IEnumerable<StockLog>>> Logs([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        return Ok(await _inventory.GetLogsAsync(limit, ct));
    }

    [HttpGet("api/health")]
    public IActionResult Health() => Ok(new { status = "ok", timeUtc = DateTime.UtcNow });
}
