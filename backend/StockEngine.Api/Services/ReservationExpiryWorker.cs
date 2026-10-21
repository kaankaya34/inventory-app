using Microsoft.Extensions.Options;

namespace StockEngine.Api.Services;

// Süresi dolan ACTIVE rezervasyonları tespit edip stoğu otomatik geri yükler (rollback).
public class ReservationExpiryWorker : BackgroundService
{
    private readonly IInventoryService _inventory;
    private readonly IStockNotifier _notifier;
    private readonly ReservationOptions _options;
    private readonly ILogger<ReservationExpiryWorker> _logger;

    public ReservationExpiryWorker(
        IInventoryService inventory,
        IStockNotifier notifier,
        IOptions<ReservationOptions> options,
        ILogger<ReservationExpiryWorker> logger)
    {
        _inventory = inventory;
        _notifier = notifier;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.CleanupIntervalSeconds));
        _logger.LogInformation("Rezervasyon temizleme servisi başladı (aralık: {Interval}s, TTL: {Ttl} dk).",
            interval.TotalSeconds, _options.TtlMinutes);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                var expired = await _inventory.ExpireDueReservationsAsync(stoppingToken);
                foreach (var result in expired)
                {
                    _logger.LogInformation("Rezervasyon {Id} süresi doldu, {Qty} adet stok iade edildi.",
                        result.Reservation?.Id, result.Reservation?.Quantity);
                    await _notifier.PublishAsync(result, ct: stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Süresi dolan rezervasyonlar işlenirken hata oluştu.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
