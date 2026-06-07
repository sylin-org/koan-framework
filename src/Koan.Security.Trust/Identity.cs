using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Hosting.App;

namespace Koan.Security.Trust;

/// <summary>
/// SEC-0001 — the ambient current-identity surface. Reads the current request's principal (populated by
/// the cookie scheme or the inbound bearer scheme — both converge here) through the ambient
/// <see cref="AppHost.Current"/> provider, the same terse-static pattern as the rest of Koan.
/// <para>Revocation (<c>Identity.Revoke</c>) lands in SEC-0001 Phase 3, once the epoch-over-coherence
/// mechanism exists — it is deliberately not exposed here as a non-functional stub.</para>
/// </summary>
public static class Identity
{
    /// <summary>
    /// The current principal as a <see cref="KoanIdentity"/>. Unauthenticated when there is no ambient
    /// request/principal (e.g. outside an HTTP request, or before startup wires <see cref="AppHost"/>).
    /// </summary>
    public static KoanIdentity Current
    {
        get
        {
            var accessor = AppHost.Current?.GetService<IHttpContextAccessor>();
            return new KoanIdentity(accessor?.HttpContext?.User);
        }
    }
}
