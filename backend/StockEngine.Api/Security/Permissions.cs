using StockEngine.Api.Models;

namespace StockEngine.Api.Security;

public record PermissionInfo(string Key, string Label, string Description);

// Yetki kataloğu (tek kaynak). ADMIN rolü tüm yetkilere sahiptir; PERSONEL (STAFF) hesaplarına
// aşağıdaki yetkiler tek tek verilir. MÜŞTERİ rolü yönetim yetkisi taşıyamaz.
public static class Permissions
{
    public const string StockView = "stock.view";
    public const string StockRestock = "stock.restock";
    public const string ReservationsManage = "reservations.manage";
    public const string SimulationRun = "simulation.run";
    public const string OrdersView = "orders.view";

    // Kullanıcı/yetki yönetimi devredilemez: yalnızca ADMIN (yetki yükseltme riskine karşı).
    public const string UsersManage = "users.manage";

    public static readonly IReadOnlyList<PermissionInfo> Catalog = new PermissionInfo[]
    {
        new(StockView, "Stok panelini görüntüleme", "Yönetim panelinde canlı stok, rezervasyon listesi ve stok hareket loglarını görür."),
        new(StockRestock, "Stok ekleme", "Depoya yeni stok ekleyebilir (+10 Stok Ekle)."),
        new(ReservationsManage, "Rezervasyon yönetimi", "El terminaliyle rezerve edebilir; başkalarının rezervasyonlarını onaylayıp iptal edebilir."),
        new(SimulationRun, "Yük testi", "Race condition (eşzamanlı istek) testini çalıştırabilir."),
        new(OrdersView, "Tüm siparişleri görüntüleme", "Tüm müşterilerin siparişlerini görür (adres detayı hariç; yalnızca il/ilçe)."),
    };

    public static readonly IReadOnlySet<string> Assignable = Catalog.Select(p => p.Key).ToHashSet();

    // ADMIN'in sahip olduğu tüm yetkiler.
    public static readonly string[] All = Catalog.Select(p => p.Key).Append(UsersManage).ToArray();

    public static string[] Parse(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(Assignable.Contains).Distinct().ToArray();

    public static string? Serialize(IEnumerable<string> permissions)
    {
        var list = permissions.Distinct().OrderBy(p => p).ToArray();
        return list.Length == 0 ? null : string.Join(',', list);
    }

    // Bir kullanıcının etkin yetkileri.
    public static string[] For(string role, string? csv) =>
        role == Roles.Admin ? All : role == Roles.Staff ? Parse(csv) : Array.Empty<string>();
}
