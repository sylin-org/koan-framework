using System.Security.Claims;

namespace Koan.Web.Auth.Roles.Contracts;

public interface IRoleMapContributor
{
    /// <summary>
    /// Contribute roles and permissions inferred from the given principal. Implementations should be resilient and never throw.
    /// </summary>
    Task ContributeAsync(ClaimsPrincipal principal, ISet<string> roles, ISet<string> permissions, RoleAttributionContext? ctx, CancellationToken ct);
}
