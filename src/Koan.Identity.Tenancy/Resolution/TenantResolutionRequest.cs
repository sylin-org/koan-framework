namespace Koan.Identity.Tenancy;

/// <summary>
/// Transport-neutral authenticated request signals supplied to an <see cref="ITenantResolver"/>. Accessors are lazy
/// so a resolver reads only the carrier it owns.
/// </summary>
/// <param name="Host">Request host without a port.</param>
/// <param name="Path">Request path.</param>
/// <param name="Subject">Canonical authenticated subject id, or <c>null</c>.</param>
/// <param name="Claim">Reads one claim value.</param>
/// <param name="Header">Reads one request header.</param>
public sealed record TenantResolutionRequest(
    string? Host,
    string? Path,
    string? Subject,
    Func<string, string?> Claim,
    Func<string, string?> Header);
