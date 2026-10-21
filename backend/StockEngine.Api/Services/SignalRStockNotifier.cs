using Microsoft.AspNetCore.SignalR;
using StockEngine.Api.Hubs;
using StockEngine.Api.Models;

namespace StockEngine.Api.Services;

public class SignalRStockNotifier : IStockNotifier
{
    private readonly IHubContext<StockHub> _hub;
    private readonly ILogger<SignalRStockNotifier> _logger;

    public SignalRStockNotifier(IHubContext<StockHub> hub, ILogger<SignalRStockNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PublishAsync(OpResult result, bool includeProduct = true, CancellationToken ct = default)
    {
        try
        {
            if (includeProduct && result.Product is not null)
                await _hub.Clients.All.SendAsync("ProductUpdated", result.Product.ToDto(), ct);

            if (result.Reservation is not null)
                await _hub.Clients.Group(StockHub.AdminGroup).SendAsync("ReservationUpdated", result.Reservation.ToDto(), ct);

            foreach (var log in result.Logs)
                await _hub.Clients.Group(StockHub.AdminGroup).SendAsync("StockLogCreated", log, ct);
        }
        catch (Exception ex)
        {
            // Bildirim hatası iş sonucunu etkilememeli (DB commit edildi).
            _logger.LogWarning(ex, "SignalR yayını başarısız oldu.");
        }
    }

    public async Task PublishProductAsync(Product product, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.All.SendAsync("ProductUpdated", product.ToDto(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR yayını başarısız oldu.");
        }
    }
}
