using Microsoft.Extensions.Hosting;

namespace Koan.Web.Auth.Server.Keys;

/// <summary>
/// SEC-0006 D1 — the fail-closed boot guard for the AS signing key. Outside Development the AS must run on a
/// persisted key: a restart on an ephemeral key would invalidate every issued token and destabilize the
/// published JWKS. Mirrors the SEC-0003 shared-secret guard shape (refuse, unless explicitly acknowledged).
/// </summary>
internal static class IssuerKeyGuard
{
    public static void EnsurePersistedOutsideDevelopment(bool isEphemeral, IHostEnvironment env, bool acknowledged)
    {
        if (!isEphemeral) return;
        if (!(env.IsProduction() || env.IsStaging())) return;
        if (acknowledged) return;

        throw new InvalidOperationException(
            "SEC-0006 fail-closed: environment '" + env.EnvironmentName + "' is running the embedded OAuth " +
            "Authorization Server on an EPHEMERAL (non-persisted) ES256 signing key. A restart would invalidate " +
            "every issued token and the published JWKS would be unstable. Reference a data adapter so the keys " +
            "persist, or set 'Koan:Web:Auth:Server:AllowEphemeralKeyOutsideDevelopment=true' to acknowledge a " +
            "throwaway deployment.");
    }
}
