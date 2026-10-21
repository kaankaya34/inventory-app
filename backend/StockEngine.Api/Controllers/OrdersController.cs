using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockEngine.Api.Models;
using StockEngine.Api.Security;
using StockEngine.Api.Services;

namespace StockEngine.Api.Controllers;

// Yönetici/el terminali akışı: sepet dışında doğrudan rezervasyon onayı. Müşteriler /api/checkout kullanır.
[ApiController]
[Authorize(Policy = Permissions.ReservationsManage)]
[Route("api/orders")]
public class OrdersController : ApiControllerBase
{
    private readonly IInventoryService _inventory;
    private readonly IStockNotifier _notifier;

    public OrdersController(IInventoryService inventory, IStockNotifier notifier)
    {
        _inventory = inventory;
        _notifier = notifier;
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmRequest request, CancellationToken ct)
    {
        var result = await _inventory.ConfirmAsync(request.ReservationId, ct);

        // Süresi dolmuş rezervasyonda stok iade edilmiş olur; durum değişikliğini yine de yayınla.
        if (result.Status == OpStatus.Expired)
            await _notifier.PublishAsync(result, ct: ct);
        if (!result.IsOk) return Error(result);

        await _notifier.PublishAsync(result, ct: ct);
        return Ok(new ConfirmResponse(result.Order!.ToDto(), result.Reservation!.ToDto(), result.Product!.ToDto()));
    }
}
