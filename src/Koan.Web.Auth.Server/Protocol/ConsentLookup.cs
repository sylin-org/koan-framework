namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0028 — what <see cref="ConsentPage.Find"/> answers: the pending request, or none; and whether the lookup was
/// refused because this address tried too many user codes in the last minute.
/// </summary>
/// <param name="Request">The pending request, or null when none waits under that id or the lookup was throttled.</param>
/// <param name="Throttled">True when the address exceeded <c>UserCodeVerificationRateLimitPerMinute</c>, the allowance
/// Koan's own <c>/oauth/request/{id}</c> shares.</param>
public sealed record ConsentLookup(PendingConsent? Request, bool Throttled);
