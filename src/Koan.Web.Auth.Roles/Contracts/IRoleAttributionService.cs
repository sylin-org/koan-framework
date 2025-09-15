using System.Security.Claims;

namespace Koan.Web.Auth.Roles.Contracts;

public interface IRoleAttributionService
{
    Task<RoleAttributionResult> ComputeAsync(ClaimsPrincipal user, RoleAttributionContext? context = null, CancellationToken ct = default);
}

public sealed record RoleAttributionContext(string? TenantId = null);

public sealed record RoleAttributionResult(IReadOnlySet<string> Roles, IReadOnlySet<string> Permissions, string? Stamp = null)
{
    public static readonly RoleAttributionResult Empty = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase), null);
}
