using System.Security.Claims;
using Koan.Web.Auth.Domain;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// Default <see cref="ICurrentUserProjector"/>. Builds the rich shape: user id, display name,
/// email, picture, roles, every cookie claim grouped by type, and linked external identities.
/// </summary>
/// <remarks>
/// The principal's claims are already issued by the cookie auth handler — surfacing them here adds
/// no leakage compared to the cookie itself (the same browser sending the cookie can read them
/// from this response). Hosts that need a stricter projection register their own
/// <see cref="ICurrentUserProjector"/> via DI; the framework registers this one with
/// <c>TryAddSingleton</c> so a host override wins.
/// </remarks>
public sealed class DefaultCurrentUserProjector : ICurrentUserProjector
{
    public Task<CurrentUserDto> Project(ClaimsPrincipal principal, IReadOnlyList<ExternalIdentity> links, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        var displayName = principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? principal.Identity?.Name;
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");
        var picture = principal.FindFirstValue("picture") ?? principal.FindFirstValue("avatar");

        var roles = principal.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var claims = principal.Claims
            .GroupBy(c => c.Type, StringComparer.Ordinal)
            .ToDictionary<IGrouping<string, Claim>, string, IReadOnlyList<string>>(
                g => g.Key,
                g => g.Select(c => c.Value).ToArray(),
                StringComparer.Ordinal);

        var connections = links.Select(x => new ConnectionDto
        {
            Provider = x.Provider,
            DisplayName = $"{displayName} ({x.Provider})",
            KeyHash = x.ProviderKeyHash,
        }).ToArray();

        var dto = new CurrentUserDto
        {
            Id = userId,
            DisplayName = displayName,
            Email = email,
            PictureUrl = picture,
            Roles = roles,
            Claims = claims,
            Connections = connections,
        };
        return Task.FromResult(dto);
    }
}
