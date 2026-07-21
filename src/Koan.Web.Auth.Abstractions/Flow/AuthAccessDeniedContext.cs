using Microsoft.AspNetCore.Http;

namespace Koan.Web.Auth.Flow;

/// <summary>
/// Context passed to <see cref="IKoanAuthFlowHandler.OnAccessDenied"/>. Mirrors
/// <see cref="AuthChallengeContext"/> but for the authenticated-but-forbidden case: same mutable
/// redirect-or-status shape, the default response is a 302 to the configured access-denied URL,
/// and a JSON/XHR handler typically sets a 403 status and marks <see cref="ResponseHandled"/>.
/// </summary>
public sealed class AuthAccessDeniedContext
{
    /// <summary>The originating request + response.</summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>Per-request service provider.</summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>The access-denied redirect URI the cookie middleware would have used.</summary>
    public required string DefaultRedirectUri { get; init; }

    /// <summary>Mutable redirect target. Default is <see cref="DefaultRedirectUri"/>.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Mark the response as fully shaped; framework suppresses its default redirect.</summary>
    public bool ResponseHandled { get; set; }
}
