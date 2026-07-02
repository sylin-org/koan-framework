using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Koan.Tenancy.Web.Authorization;

/// <summary>
/// The posture-aware, layered AUTHORITY gate for the tenancy control-plane console (ARCH-0104). Composes, all
/// fail-closed:
/// <list type="bullet">
/// <item><b>Open (Development)</b> — auto-admit (self-announcing at boot). Optionally restricted to loopback via
/// <see cref="TenancyConsoleOptions.RequireLoopbackForOpenPosture"/> so a public dev bind can't expose an ungated
/// console.</item>
/// <item><b>Closed (Production)</b> — admit an authenticated principal that either carries the grant
/// <see cref="ConsoleGrantOptions.Role"/> (e.g. bound via <c>Koan.Identity</c>'s <c>IdentityRole</c>) <b>or</b> whose
/// identity is in the break-glass <see cref="ConsoleGrantOptions.Operators"/> allow-list. Both are keyed on
/// identity/role, never on the forgeable request-shape (that's the exposure layer's job); neither is derived from a
/// tenant membership ("no master backdoor").</item>
/// </list>
/// Fail-closed by construction: the handler only ever calls <see cref="AuthorizationHandlerContext.Succeed"/>; a
/// principal satisfying no branch is denied (403). The exposure layer (a separate middleware) has already decided the
/// console is served here at all.
/// </summary>
public sealed class OperatorAuthorizationHandler : AuthorizationHandler<OperatorRequirement>
{
    private readonly TenancyRuntime _runtime;
    private readonly IOptions<TenancyConsoleOptions> _options;
    private readonly IHttpContextAccessor _http;

    public OperatorAuthorizationHandler(TenancyRuntime runtime, IOptions<TenancyConsoleOptions> options, IHttpContextAccessor http)
    {
        _runtime = runtime;
        _options = options;
        _http = http;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperatorRequirement requirement)
    {
        var o = _options.Value;

        if (_runtime.Posture == TenancyPosture.Open)
        {
            // Dev-open: just works — optionally only from loopback. (Prod can never reach Open: the tenancy boot
            // pre-flight refuses a forced-Open-in-prod boot before this handler ever runs.)
            if (!o.RequireLoopbackForOpenPosture || IsLoopback())
                context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Prod-closed: authority is the composed OR of an explicit role claim and the break-glass identity allow-list.
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true && (HasGrantedRole(user, o.Grant) || IsAllowListed(user, o.Grant)))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    private static bool HasGrantedRole(ClaimsPrincipal user, ConsoleGrantOptions grant)
        => !string.IsNullOrEmpty(grant.Role) && user.IsInRole(grant.Role);

    private static bool IsAllowListed(ClaimsPrincipal user, ConsoleGrantOptions grant)
    {
        if (grant.Operators is null || grant.Operators.Length == 0) return false;
        foreach (var candidate in Identities(user))
            if (grant.Operators.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static System.Collections.Generic.IEnumerable<string> Identities(ClaimsPrincipal user)
    {
        string?[] raw =
        {
            user.Identity?.Name,
            user.FindFirst("sub")?.Value,
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            user.FindFirst(ClaimTypes.Email)?.Value,
            user.FindFirst("email")?.Value,
        };
        return raw.Where(v => !string.IsNullOrWhiteSpace(v))!.Cast<string>();
    }

    private bool IsLoopback()
    {
        var remote = _http.HttpContext?.Connection?.RemoteIpAddress;
        // No connection info (e.g. an in-process test) is treated as loopback — the restriction targets real remote binds.
        return remote is null || IPAddress.IsLoopback(remote);
    }
}
