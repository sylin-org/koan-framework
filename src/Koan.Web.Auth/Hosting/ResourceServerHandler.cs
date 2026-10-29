using System.Text.Encodings.Web;
using Koan.Security.Trust.Inbound;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// SEC-0025 — the resource edge of ordinary requests (<see cref="AuthenticationExtensions.ResourceServerScheme"/>). A
/// request without a <c>Bearer</c> <c>Authorization</c> header — no header, or one of another scheme, such as the
/// <c>Basic</c> credentials a reverse proxy passes upstream — is forwarded whole to the session cookie. A request with
/// one is this handler's alone, so a cookie it also carries is never read: its bearer is accepted through Trust's
/// <see cref="KoanBearerResource"/> for this application's resource, and anything else — an empty token, a token that
/// is not authentic, one for another audience — is a failure, answered on challenge with 401 and
/// <c>WWW-Authenticate: Bearer error="invalid_token"</c>.
/// </summary>
internal sealed class ResourceServerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthOptions> auth,
    KoanWebBaseUrl baseUrl)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme a request is authenticated by: this one when it carries a bearer, else the session cookie.</summary>
    internal static string? Forward(HttpContext context)
        => CarriesBearer(context.Request) ? null : AuthenticationExtensions.CookieScheme;

    /// <summary>
    /// Whether <paramref name="request"/> presents a bearer: an <c>Authorization</c> header whose scheme is <c>Bearer</c>,
    /// compared without case (RFC 9110 §11.1). A header of any other scheme carries no token this edge could accept.
    /// </summary>
    internal static bool CarriesBearer(HttpRequest request)
    {
        foreach (var value in request.Headers.Authorization)
        {
            var header = value.AsSpan().TrimStart();
            if (header.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase)
                && (header.Length == Bearer.Length || header[Bearer.Length] is ' ' or '\t'))
                return true;
        }
        return false;
    }

    private const string Bearer = "Bearer";

    /// <summary>The resource a token must be issued for: the configured identifier, else the application's base URL.</summary>
    internal string Resource => auth.Value.ResourceServer.Resource ?? baseUrl.Resolve(Context);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var result = await KoanBearerResource.Authenticate(Context, Resource).ConfigureAwait(false);
        return result.None ? AuthenticateResult.Fail("The Authorization header carries an empty bearer token.") : result;
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await Context.AuthenticateAsync(Scheme.Name).ConfigureAwait(false);
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = result.Succeeded
            ? KoanBearerResource.Challenge()
            : KoanBearerResource.Challenge(KoanBearerResource.InvalidToken);
    }
}
