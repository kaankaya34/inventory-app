using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockEngine.Api.Models;
using StockEngine.Api.Security;
using StockEngine.Api.Services;

namespace StockEngine.Api.Controllers;

// Giriş yapmış müşteriye ait mağaza uç noktaları. Kullanıcı kimliği her zaman JWT'den alınır.
[ApiController]
[Authorize]
public class ShopController : ApiControllerBase
{
    private readonly ShopService _shop;
    private readonly AuthService _auth;

    public ShopController(ShopService shop, AuthService auth)
    {
        _shop = shop;
        _auth = auth;
    }

    // ---- sepet
    [HttpGet("api/cart")]
    public async Task<CartDto> GetCart(CancellationToken ct) => await _shop.GetCartAsync(CurrentUserId, ct);

    [HttpPost("api/cart/items")]
    public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request, CancellationToken ct) =>
        From(await _shop.AddToCartAsync(CurrentUserId, request, ct));

    [HttpDelete("api/cart/items/{reservationId:guid}")]
    public async Task<IActionResult> RemoveFromCart(Guid reservationId, CancellationToken ct) =>
        From(await _shop.RemoveFromCartAsync(CurrentUserId, reservationId, ct));

    // ---- ödeme
    [HttpPost("api/checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request, CancellationToken ct) =>
        From(await _shop.CheckoutAsync(CurrentUserId, request, ct));

    // ---- siparişlerim
    [HttpGet("api/my/orders")]
    public async Task<IReadOnlyList<CustomerOrderDto>> MyOrders(CancellationToken ct) =>
        await _shop.GetOrdersAsync(CurrentUserId, ct);

    // ---- kayıtlı kartlar (numara asla döndürülmez; yalnızca marka + son 4 hane)
    [HttpGet("api/my/cards")]
    public async Task<IReadOnlyList<SavedCardDto>> MyCards(CancellationToken ct) =>
        await _shop.GetCardsAsync(CurrentUserId, ct);

    [HttpDelete("api/my/cards/{cardId:guid}")]
    public async Task<IActionResult> DeleteCard(Guid cardId, CancellationToken ct) =>
        await _shop.DeleteCardAsync(CurrentUserId, cardId, ct)
            ? NoContent()
            : NotFound(new ApiError(false, "CARD_NOT_FOUND", "Kart bulunamadı."));

    // ---- profil
    [HttpGet("api/my/profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct) =>
        From(await _auth.GetProfileAsync(CurrentUserId, ct));

    [HttpPut("api/my/profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct) =>
        From(await _auth.UpdateProfileAsync(CurrentUserId, request, ct));
}
