using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockEngine.Api.Models;
using StockEngine.Api.Security;
using StockEngine.Api.Services;

namespace StockEngine.Api.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController : ApiControllerBase
{
    private readonly IInventoryService _inventory;
    private readonly IStockNotifier _notifier;

    public InventoryController(IInventoryService inventory, IStockNotifier notifier)
    {
        _inventory = inventory;
        _notifier = notifier;
    }

    // El terminali (personel): müşteriler sepeti /api/cart üzerinden kullanır, bu uçları değil.
    [Authorize(Policy = Permissions.ReservationsManage)]
    [HttpPost("reserve")]
    public async Task<IActionResult> Reserve([FromBody] ReserveRequest request, CancellationToken ct)
    {
        var userId = !string.IsNullOrWhiteSpace(request.UserId) ? request.UserId : $"STAFF-{CurrentUserId}";

        var result = await _inventory.ReserveAsync(request.Barcode, request.Quantity, userId, ct);
        if (!result.IsOk) return Error(result);

        await _notifier.PublishAsync(result, ct: ct);
        var r = result.Reservation!.ToDto();
        return Ok(new ReserveResponse(r.Id, r.Barcode, r.ProductName, r.Quantity, r.Status,
            r.ExpiresAt, r.RemainingSeconds, result.Product!.AvailableStock));
    }

    [Authorize(Policy = Permissions.ReservationsManage)]
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel([FromBody] CancelRequest request, CancellationToken ct)
    {
        var result = await _inventory.CancelAsync(request.ReservationId, null, ct);
        if (!result.IsOk) return Error(result);

        await _notifier.PublishAsync(result, ct: ct);
        return Ok(new CancelResponse(result.Reservation!.ToDto(), result.Product!.ToDto()));
    }

    [Authorize(Policy = Permissions.StockRestock)]
    [HttpPost("restock")]
    public async Task<IActionResult> Restock([FromBody] RestockRequest request, CancellationToken ct)
    {
        var result = await _inventory.RestockAsync(request.Barcode, request.AddQuantity, ct);
        if (!result.IsOk) return Error(result);

        await _notifier.PublishAsync(result, ct: ct);
        return Ok(new RestockResponse(result.Product!.ToDto(), request.AddQuantity));
    }
}
