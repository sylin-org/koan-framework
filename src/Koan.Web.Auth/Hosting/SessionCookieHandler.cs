using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// SEC-0037 — <c>Koan.cookie</c>. A browser sends its session cookie whoever asks, so one that no longer holds (revoked, expired,
/// unreadable after keys or a data folder changed) is no credential presented: its holder is a visitor, on every route, and signs
/// in again. A rejected credential stays rejected where a caller presented one, as a bearer is (WEB-0083).
/// </summary>
internal sealed class SessionCookieHandler(IOptionsMonitor<CookieAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : CookieAuthenticationHandler(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var result = await base.HandleAuthenticateAsync().ConfigureAwait(false);
        if (result.Failure is not { } failure) return result;
        Logger.LogDebug("A session cookie no longer holds, and reads as a visitor: {Failure}", failure.Message);
        return AuthenticateResult.NoResult();
    }
}
