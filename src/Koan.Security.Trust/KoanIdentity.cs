using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Koan.Security.Trust;

/// <summary>
/// SEC-0001 — a channel-agnostic read of the current principal (whether it arrived by cookie or by the
/// inbound bearer scheme), projecting the coarse identity the fabric standardizes on. Produced by
/// <see cref="Identity.Current"/>.
/// </summary>
public readonly struct KoanIdentity
{
    private readonly ClaimsPrincipal? _principal;

    internal KoanIdentity(ClaimsPrincipal? principal) => _principal = principal;

    /// <summary>The underlying principal, if any.</summary>
    public ClaimsPrincipal? Principal => _principal;

    public bool IsAuthenticated => _principal?.Identity?.IsAuthenticated == true;

    /// <summary>Subject id — <c>sub</c> for a bearer KSVID, <c>NameIdentifier</c> for a cookie principal.</summary>
    public string? Id =>
        _principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? _principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public string? Name =>
        _principal?.Identity?.Name
        ?? _principal?.FindFirst(ClaimTypes.Name)?.Value
        ?? _principal?.FindFirst(JwtRegisteredClaimNames.Name)?.Value;

    /// <summary>Coarse roles (<c>ClaimTypes.Role</c>) — the only claim authorization keys on (SEC-0001 §8).</summary>
    public IReadOnlyCollection<string> Roles =>
        _principal is null
            ? Array.Empty<string>()
            : _principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

    /// <summary>
    /// True if the principal holds <paramref name="role"/>. Checks <c>ClaimTypes.Role</c> claims directly
    /// (not <c>IsInRole</c>), so it is robust across cookie and bearer principals regardless of the
    /// scheme's configured role-claim type.
    /// </summary>
    public bool Is(string role) =>
        _principal is not null &&
        _principal.FindAll(ClaimTypes.Role).Any(c => string.Equals(c.Value, role, StringComparison.Ordinal));
}
