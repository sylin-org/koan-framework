using System.Text.RegularExpressions;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// Internal constants used by the framework's auth lifecycle wiring (cookie events, contributor
/// dispatch). Surface kept minimal to avoid leaking implementation details to apps.
/// </summary>
internal static class AuthLifecycleConstants
{
    /// <summary>
    /// Compiled regex matching Koan's OAuth callback route <c>/auth/{provider}/callback</c>.
    /// Captures the provider segment so the lifecycle dispatcher can surface it on
    /// <c>AuthSignInContext.Provider</c>.
    /// </summary>
    public static readonly Regex CallbackPathRegex = new(
        @"^/auth/([^/]+)/callback$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

/// <summary>
/// Keys for <c>HttpContext.Items</c> markers the framework writes from cookie event handlers so
/// outer middleware can react (e.g. translate a sign-in rejection into a redirect). Outer
/// middleware reads — does not write — these markers.
/// </summary>
public static class AuthLifecycleMarkers
{
    /// <summary>Set on <c>HttpContext.Items</c> when an <c>IKoanAuthEventContributor</c> called
    /// <c>AuthSignInContext.Reject(...)</c> during sign-in. The value is the reject reason string.</summary>
    public const string SignInRejected = "Koan.Web.Auth.SignInRejected";
}
