using System.Security.Claims;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0005 — the canonical subject id read from a principal: <c>sub</c> (a bearer token) then
/// <c>NameIdentifier</c> (a cookie principal), matching SEC-0001's <c>KoanIdentity.Id</c>. Used by grant
/// materialization and mutation audit so both attribute to the same id. An anonymous principal has neither → null.
/// </summary>
internal static class AuthSubject
{
    public static string? Id(ClaimsPrincipal? principal)
    {
        if (principal is null) return null;
        var sub = principal.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(sub)) return sub;
        var nameId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(nameId) ? null : nameId;
    }
}
