namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// Why a sign-in did not complete, in the fixed vocabulary Koan carries to the application's failure landing
/// (WEB-0080). A handler names one of these; the person's browser receives its code, never the provider's text.
/// </summary>
public enum SignInFailure
{
    /// <summary>Anything not named below: the catch-all a handler answers when it cannot say more safely.</summary>
    Failed = 0,

    /// <summary>The provider could not be reached, or answered something that is not a usable provider.</summary>
    ProviderUnreachable,

    /// <summary>The account handle or identifier could not be resolved to an identity.</summary>
    HandleUnresolved,

    /// <summary>The person refused at the provider.</summary>
    AccessDenied,

    /// <summary>The callback did not match the sign-in that was started: state, correlation or issuer.</summary>
    CallbackMismatch,

    /// <summary>
    /// Binding a login needs the person it binds to signed in, recently where <c>ReConsent:ForceOnLink</c> asks: they sign
    /// in, then bind.
    /// </summary>
    SignInFirst,

    /// <summary>The login being bound is another person's, and stays theirs.</summary>
    LoginTaken,
}
