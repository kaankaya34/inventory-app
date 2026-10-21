using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockEngine.Api.Models;
using StockEngine.Api.Security;
using StockEngine.Api.Services;

namespace StockEngine.Api.Controllers;

[ApiController]
[Authorize(Policy = Permissions.SimulationRun)]
[Route("api/simulation")]
public class SimulationController : ApiControllerBase
{
    private const int DefaultRequestCount = 100;
    private const int MaxRequestCount = 500;

    private readonly IInventoryService _inventory;
    private readonly IStockNotifier _notifier;

    public SimulationController(IInventoryService inventory, IStockNotifier notifier)
    {
        _inventory = inventory;
        _notifier = notifier;
    }

    /// <summary>
    /// Backend içinden Task.WhenAll ile aynı anda N adet rezervasyon isteği ateşler.
    /// Tüm görevler bir "başlangıç kapısında" bekletilip aynı anda serbest bırakılır.
    /// </summary>
    [HttpPost("race-condition")]
    public async Task<IActionResult> RaceCondition([FromBody] SimulationRequest request, CancellationToken ct)
    {
        var barcode = request.Barcode?.Trim();
        var count = request.RequestCount ?? DefaultRequestCount;
        var quantity = request.Quantity ?? 1;

        if (string.IsNullOrEmpty(barcode))
            return BadRequest(new ApiError(false, "INVALID_REQUEST", "Geçerli bir barkod gereklidir."));
        if (count < 1 || count > MaxRequestCount)
            return BadRequest(new ApiError(false, "INVALID_REQUEST", $"requestCount 1 ile {MaxRequestCount} arasında olmalıdır."));
        if (quantity < 1)
            return BadRequest(new ApiError(false, "INVALID_REQUEST", "quantity en az 1 olmalıdır."));

        var initial = await _inventory.GetProductByBarcodeAsync(barcode, ct);
        if (initial is null)
            return NotFound(new ApiError(false, "PRODUCT_NOT_FOUND", "Ürün bulunamadı."));

        var runId = Guid.NewGuid().ToString("N")[..6];
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(1, count).Select(i => Task.Run(async () =>
        {
            var userId = $"SIM-{runId}-{i:D3}";
            await gate.Task;
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await _inventory.ReserveAsync(barcode, quantity, userId, CancellationToken.None);
                sw.Stop();
                return (Index: i, UserId: userId, Result: result, Ms: sw.Elapsed.TotalMilliseconds, Error: false);
            }
            catch
            {
                sw.Stop();
                return (Index: i, UserId: userId, Result: (OpResult?)null, Ms: sw.Elapsed.TotalMilliseconds, Error: true);
            }
        })).ToArray();

        var total = Stopwatch.StartNew();
        gate.SetResult();
        var outcomes = await Task.WhenAll(tasks);
        total.Stop();

        // Her başarılı rezervasyon ve log istemcilere iletilir; ürün durumu ise tek seferde (son hali) yayınlanır.
        foreach (var o in outcomes.Where(o => o.Result is { IsOk: true }))
            await _notifier.PublishAsync(o.Result!, includeProduct: false, ct);

        var final = await _inventory.GetProductByBarcodeAsync(barcode, ct) ?? initial;
        await _notifier.PublishProductAsync(final, ct);

        var items = outcomes.Select(o => new SimulationItem(
            o.Index, o.UserId,
            o.Result is { IsOk: true },
            o.Error ? "ERROR" : o.Result!.IsOk ? "RESERVED" : o.Result.Status == OpStatus.InsufficientStock ? "INSUFFICIENT_STOCK" : o.Result.Status.ToString().ToUpperInvariant(),
            Math.Round(o.Ms, 2),
            o.Result?.Reservation?.Id)).ToList();

        var success = items.Count(i => i.Success);
        var errors = items.Count(i => i.Status == "ERROR");
        var failed = items.Count - success - errors;
        var durations = items.Select(i => i.DurationMs).OrderBy(d => d).ToArray();
        var p95 = durations[Math.Min(durations.Length - 1, (int)Math.Ceiling(durations.Length * 0.95) - 1)];

        var report = new SimulationReport(
            final.Barcode, final.Name, count, quantity,
            success, failed, errors,
            initial.AvailableStock, final.AvailableStock, final.ReservedStock,
            OversellingPrevented: final.AvailableStock >= 0 && success * quantity <= initial.AvailableStock,
            StockConsistent: final.PhysicalStock == final.ReservedStock + final.AvailableStock
                             && initial.AvailableStock - final.AvailableStock == success * quantity,
            Math.Round(total.Elapsed.TotalMilliseconds, 2),
            durations[0], Math.Round(durations.Average(), 2), p95, durations[^1],
            items);

        return Ok(report);
    }
}
