using System.Security.Claims;
using Koan.Web.Auth.Roles.Contracts;

namespace Koan.Web.Auth.Roles.Contributors;

/// <summary>
/// A minimal built-in contributor that currently does nothing beyond the base extraction.
/// Placeholder for future provider-specific mappings. Kept non-empty per project policies.
/// </summary>
public sealed class DefaultCapabilityContributor : IRoleMapContributor
{
    public Task ContributeAsync(ClaimsPrincipal principal, ISet<string> roles, ISet<string> permissions, RoleAttributionContext? ctx, CancellationToken ct)
    {
        // No-op in V1: we rely on raw claim extraction and policy-to-role binding.
        return Task.CompletedTask;
    }
}
