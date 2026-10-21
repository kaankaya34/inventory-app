using Microsoft.AspNetCore.Mvc;
using StockEngine.Api.Models;
using StockEngine.Api.Services;

namespace StockEngine.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ApiControllerBase
{
    private readonly IInventoryService _inventory;

    public ProductsController(IInventoryService inventory) => _inventory = inventory;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetAll(CancellationToken ct)
    {
        var products = await _inventory.GetProductsAsync(ct);
        return Ok(products.Select(p => p.ToDto()));
    }

    [HttpGet("barcode/{barcode}")]
    public async Task<IActionResult> GetByBarcode(string barcode, CancellationToken ct)
    {
        var product = await _inventory.GetProductByBarcodeAsync(barcode, ct);
        return product is null
            ? NotFound(new ApiError(false, "PRODUCT_NOT_FOUND", "Ürün bulunamadı."))
            : Ok(product.ToDto());
    }
}
