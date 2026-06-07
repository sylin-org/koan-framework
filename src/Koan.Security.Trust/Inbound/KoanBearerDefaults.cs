namespace Koan.Security.Trust.Inbound;

/// <summary>SEC-0001 — the inbound trust-fabric bearer authentication scheme.</summary>
public static class KoanBearerDefaults
{
    /// <summary>
    /// The bearer scheme name. Non-default and opt-in: protect token endpoints with
    /// <c>[Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]</c> so the bearer
    /// handler's own 401 challenge fires, leaving the cookie scheme's redirect behaviour untouched.
    /// </summary>
    public const string AuthenticationScheme = "Koan.bearer";
}
