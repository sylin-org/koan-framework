using System.Security.Claims;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Http;

namespace Koan.Identity.Web;

/// <summary>Resolves the effective request subject; audit attribution continues to use the separate real-actor accessor.</summary>
internal sealed class HttpContextScopedRoleSubjectAccessor(IHttpContextAccessor http) : IScopedRoleSubjectAccessor
{
    public string? CurrentSubject
        => http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? http.HttpContext?.User.FindFirst("sub")?.Value;
}
