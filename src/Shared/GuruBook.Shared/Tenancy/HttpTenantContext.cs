using System.Security.Claims;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Http;

namespace GuruBook.Shared.Tenancy;

/// <summary>Reads the tenant and user from the signed-in user's claims.</summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{

    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? TenantId => ParseGuid(User?.FindFirstValue(GuruBookClaims.TenantId));

    public Guid? UserId => ParseGuid(User?.FindFirstValue(GuruBookClaims.Subject));

    public bool IsSuperAdmin => User?.IsInRole(Roles.SuperAdmin) ?? false;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
