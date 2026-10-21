using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using StockEngine.Api.Models;

namespace StockEngine.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    protected bool IsAdmin => User.IsInRole(Roles.Admin);

    // ADMIN her yetkiye sahiptir; diğerleri DB'den gelen "perm" claim'lerine bakılır (bkz. Program.cs OnTokenValidated).
    protected bool Can(string permission) => IsAdmin || User.HasClaim("perm", permission);

    protected IActionResult From<T>(ServiceResult<T> r) =>
        r.Ok
            ? StatusCode(r.Status, r.Value)
            : StatusCode(r.Status, new ApiError(false, r.Code, r.Message));

    protected IActionResult Error(OpResult r)
    {
        var (status, code) = r.Status switch
        {
            OpStatus.Invalid => (StatusCodes.Status400BadRequest, "INVALID_REQUEST"),
            OpStatus.ProductNotFound => (StatusCodes.Status404NotFound, "PRODUCT_NOT_FOUND"),
            OpStatus.ReservationNotFound => (StatusCodes.Status404NotFound, "RESERVATION_NOT_FOUND"),
            OpStatus.InsufficientStock => (StatusCodes.Status409Conflict, "INSUFFICIENT_STOCK"),
            OpStatus.NotActive => (StatusCodes.Status409Conflict, "RESERVATION_NOT_ACTIVE"),
            OpStatus.Expired => (StatusCodes.Status410Gone, "RESERVATION_EXPIRED"),
            _ => (StatusCodes.Status500InternalServerError, "UNKNOWN")
        };
        return StatusCode(status, new ApiError(false, code, r.Message, r.AvailableStock));
    }
}
