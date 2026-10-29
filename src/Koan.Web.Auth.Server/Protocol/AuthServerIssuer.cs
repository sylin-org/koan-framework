using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0023 — the authorization server's one issuer identifier: <see cref="AuthServerOptions.Issuer"/> when set, else
/// the application's named <c>Koan:Security:Trust:Issuer</c>, else the canonical base URL (WEB-0078; derived from the
/// request where none is configured). The discovery metadata states it, and Trust stamps it on every token the
/// application issues and accepts it at <c>Koan.bearer</c> through <see cref="IIssuerIdentifier"/>, so an outside
/// verifier that reads the metadata and the JWKS accepts the tokens.
/// </summary>
internal sealed class AuthServerIssuer(
    IOptions<AuthServerOptions> server,
    IOptions<TrustIssuerOptions> trust,
    KoanWebBaseUrl baseUrl,
    IHttpContextAccessor accessor) : IIssuerIdentifier
{
    /// <summary>
    /// The issuer for <paramref name="context"/>, as the discovery metadata states it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Nothing names the issuer and Production refuses to derive it from the
    /// request (WEB-0078).</exception>
    public string For(HttpContext context) => Named() ?? baseUrl.Resolve(context);

    /// <inheritdoc />
    public string? Resolve()
    {
        if (Named() is { } named) return named;
        if (accessor.HttpContext is not { } context) return null;
        try
        {
            return baseUrl.Resolve(context);
        }
        catch (InvalidOperationException)
        {
            // Production refuses a request-derived origin (WEB-0078); the metadata refuses with the correction, and a
            // token keeps Trust's own name rather than one a caller's Host header chose.
            return null;
        }
    }

    private string? Named()
    {
        var issuer = server.Value.Issuer;
        if (!string.IsNullOrWhiteSpace(issuer)) return issuer.TrimEnd('/');
        return trust.Value.Issuer ?? baseUrl.Configured;
    }
}
