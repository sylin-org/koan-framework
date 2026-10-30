namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0081: one origin requests arrived on other than the base URL in force — scheme, host and port as the
/// trusted-proxy posture resolved them, normalised like a base URL — with how often and when it was seen.
/// </summary>
/// <param name="Origin">The origin, such as <c>https://www.example.com</c> or <c>http://127.0.0.1:5220</c>.</param>
/// <param name="AddressClass">What kind of address its host is.</param>
/// <param name="Count">How many requests arrived on it while it was held.</param>
/// <param name="FirstSeen">When the first of them arrived.</param>
/// <param name="LastSeen">When the latest of them arrived.</param>
public sealed record ObservedOrigin(
    string Origin,
    OriginAddressClass AddressClass,
    long Count,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen)
{
    /// <summary>True for a loopback or unspecified address: an origin only this machine reaches.</summary>
    public bool IsLocal => AddressClass is OriginAddressClass.Loopback or OriginAddressClass.Unspecified;
}
