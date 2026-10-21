using Microsoft.AspNetCore.SignalR;
using StockEngine.Api.Models;
using StockEngine.Api.Security;

namespace StockEngine.Api.Hubs;

// Sunucu -> istemci olayları:
//   ProductUpdated(ProductDto)            -> herkese (mağaza canlı stok)
//   ReservationUpdated(ReservationDto)    -> yalnızca ADMIN grubuna
//   StockLogCreated(StockLog)             -> yalnızca ADMIN grubuna
// Admin olduğunu JWT ile (access_token) kanıtlayan bağlantılar otomatik olarak ADMIN grubuna eklenir.
public class StockHub : Hub
{
    public const string Path = "/hubs/stock";
    public const string AdminGroup = "admins";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        // Rezervasyon/log olayları yalnızca stok panelini görme yetkisi olanlara (ve ADMIN'e) gider.
        if (user?.IsInRole(Roles.Admin) == true || user?.HasClaim("perm", Permissions.StockView) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
        await base.OnConnectedAsync();
    }
}
