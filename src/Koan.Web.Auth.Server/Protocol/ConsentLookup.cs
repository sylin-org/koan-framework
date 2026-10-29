namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0028 — what <see cref="ConsentPage.Find"/> answers: the pending request, or none; whether the lookup was refused
/// because this address tried too many user codes in the last minute; and whether a device request waits under the code
/// for a client that is not allowed the Device Authorization Grant (SEC-0024).
/// </summary>
/// <param name="Request">The pending request, or null when none waits under that id, the lookup was throttled, or its
/// client is not allowed its grant.</param>
/// <param name="Throttled">True when the address exceeded <c>UserCodeVerificationRateLimitPerMinute</c>, the allowance
/// Koan's own <c>/oauth/request/{id}</c> shares.</param>
public sealed record ConsentLookup(PendingConsent? Request, bool Throttled)
{
    /// <summary>
    /// True when a pending device request waits under the code but its client is not, or no longer, allowed the Device
    /// Authorization Grant. The request is not returned, so its client's self-chosen name never reaches the page;
    /// approving it and exchanging its device code are refused with <c>unauthorized_client</c>. Outside the positional
    /// members, so the record's constructor and deconstruction keep their two slots.
    /// </summary>
    public bool ClientNotAllowed { get; init; }
}
