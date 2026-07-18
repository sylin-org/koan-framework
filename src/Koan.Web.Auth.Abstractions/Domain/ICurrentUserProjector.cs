using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Web.Auth.Domain;

/// <summary>
/// Projects a signed-in <see cref="ClaimsPrincipal"/> into the <see cref="CurrentUserDto"/> returned
/// by <c>GET /me</c>. Pluggable so a host application can decide exactly which claims surface to
/// front-end consumers (e.g. include roles + email by default, redact them in a privacy-sensitive
/// deployment, or attach custom fields not in the principal).
/// </summary>
/// <remarks>
/// <para>
/// The functional package's default implementation surfaces the rich shape:
/// user id, display name, email, picture, roles, all custom claims grouped by type, and the linked
/// provider connections. This matches the contract platform SPAs typically need to render
/// role-gated UI without an extra probe round-trip.
/// </para>
/// <para>
/// Replace through standard DI; functional Web Auth registers its default with <c>TryAddSingleton</c> so a host
/// registration wins without requiring a manual framework activation call.
/// </para>
/// </remarks>
public interface ICurrentUserProjector
{
    /// <summary>
    /// Build the <c>/me</c> response from the current principal and the user's linked external
    /// identities. Implementations should treat <paramref name="principal"/> and
    /// <paramref name="links"/> as read-only.
    /// </summary>
    Task<CurrentUserDto> Project(ClaimsPrincipal principal, IReadOnlyList<ExternalIdentity> links, CancellationToken ct);
}
