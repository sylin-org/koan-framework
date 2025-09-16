using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Koan.Web.Extensions.Authorization;

internal sealed class CapabilityAuthorizer : ICapabilityAuthorizer
{
    private readonly CapabilityAuthorizationOptions _options;
    private readonly IAuthorizationService _authz;

    public CapabilityAuthorizer(CapabilityAuthorizationOptions options, IAuthorizationService authz)
    {
        _options = options;
        _authz = authz;
    }

    public bool IsAllowed(ClaimsPrincipal user, Type entityType, string capabilityAction)
    {
        // Resolve mapping: Entity → Defaults → DefaultBehavior
        var entityName = entityType.Name;
        var policyName = ResolvePolicyName(entityName, capabilityAction);
        if (policyName is null)
        {
            return _options.DefaultBehavior == CapabilityDefaultBehavior.Allow;
        }

        // If a policy name is configured, use ASP.NET AuthorizationService to evaluate.
        var result = _authz.AuthorizeAsync(user, null, policyName).GetAwaiter().GetResult();
        return result.Succeeded;
    }

    private string? ResolvePolicyName(string entityName, string action)
    {
        if (_options.Entities.TryGetValue(entityName, out var entityPolicy))
        {
            var p = Pick(entityPolicy, action);
            if (!string.IsNullOrWhiteSpace(p)) return p;
        }
        var d = Pick(_options.Defaults, action);
        return string.IsNullOrWhiteSpace(d) ? null : d;
    }

    private static string? Pick(CapabilityPolicy policy, string action)
    {
        return action switch
        {
            Capabilities.CapabilityActions.Moderation.DraftCreate => policy.Moderation.DraftCreate,
            Capabilities.CapabilityActions.Moderation.DraftUpdate => policy.Moderation.DraftUpdate,
            Capabilities.CapabilityActions.Moderation.DraftGet => policy.Moderation.DraftGet,
            Capabilities.CapabilityActions.Moderation.Submit => policy.Moderation.Submit,
            Capabilities.CapabilityActions.Moderation.Withdraw => policy.Moderation.Withdraw,
            Capabilities.CapabilityActions.Moderation.Queue => policy.Moderation.Queue,
            Capabilities.CapabilityActions.Moderation.Approve => policy.Moderation.Approve,
            Capabilities.CapabilityActions.Moderation.Reject => policy.Moderation.Reject,
            Capabilities.CapabilityActions.Moderation.Return => policy.Moderation.Return,

            Capabilities.CapabilityActions.SoftDelete.ListDeleted => policy.SoftDelete.ListDeleted,
            Capabilities.CapabilityActions.SoftDelete.Delete => policy.SoftDelete.Delete,
            Capabilities.CapabilityActions.SoftDelete.DeleteMany => policy.SoftDelete.DeleteMany,
            Capabilities.CapabilityActions.SoftDelete.Restore => policy.SoftDelete.Restore,
            Capabilities.CapabilityActions.SoftDelete.RestoreMany => policy.SoftDelete.RestoreMany,

            Capabilities.CapabilityActions.Audit.Snapshot => policy.Audit.Snapshot,
            Capabilities.CapabilityActions.Audit.List => policy.Audit.List,
            Capabilities.CapabilityActions.Audit.Revert => policy.Audit.Revert,

            _ => null
        };
    }
}