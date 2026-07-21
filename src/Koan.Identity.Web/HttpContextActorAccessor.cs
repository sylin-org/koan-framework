using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Koan.Identity;
using Koan.Identity.Impersonation;

namespace Koan.Identity.Web;

/// <summary>
/// SEC-0007 D8 — supplies the acting subject for audit attribution from the current request principal, so every
/// identity/access mutation records WHO performed it. When impersonating, the actor is the real operator (the
/// <c>koan_actor</c> claim, not the impersonated target); otherwise the principal's own subject. Best-effort: null
/// outside a request (e.g. a background mutation), matching the audit's best-effort contract.
/// </summary>
internal sealed class HttpContextActorAccessor : IIdentityActorAccessor
{
    private readonly IHttpContextAccessor _http;

    public HttpContextActorAccessor(IHttpContextAccessor http) => _http = http;

    public string? CurrentActorSubject
    {
        get
        {
            var user = _http.HttpContext?.User;
            if (user is null) return null;
            return ImpersonationClaims.ActorOf(user)
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;
        }
    }
}
