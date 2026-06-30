using System.Security.Claims;

namespace FinViet.Api.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetCustomerId(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst("customerId") ?? user.FindFirst(ClaimTypes.NameIdentifier);
        if (claim is null || !Guid.TryParse(claim.Value, out var id))
            throw new UnauthorizedAccessException("Customer id claim is missing or invalid.");
        return id;
    }
}