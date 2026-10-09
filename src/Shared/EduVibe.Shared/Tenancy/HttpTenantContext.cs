using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace EduVibe.Shared.Tenancy;

/// <summary>Reads the tenant and user from the signed-in user's claims.</summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public const string TenantIdClaim = "tenant_id";
    public const string SuperAdminRole = "SuperAdmin";

    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? TenantId => ParseGuid(User?.FindFirstValue(TenantIdClaim));

    public Guid? UserId => ParseGuid(User?.FindFirstValue(ClaimTypes.NameIdentifier));

    public bool IsSuperAdmin => User?.IsInRole(SuperAdminRole) ?? false;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
