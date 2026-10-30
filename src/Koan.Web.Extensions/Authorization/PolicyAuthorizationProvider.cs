using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Koan.Web.Extensions.Capabilities;
using Koan.Web.Hooks;
using Koan.Web.Authorization;

namespace Koan.Web.Extensions.Authorization;

/// <summary>
/// SEC-0002 §5/§7 — the named-policy provider (Order 100). Absorbs WEB-0047's capability resolution
/// (Entity-mapping → Defaults → DefaultBehavior) and delegates mapped policies to ASP.NET
/// <see cref="IAuthorizationService"/>. It is <b>definitive</b> for recognized capability actions, so an action with no
/// policy never reaches the seam's allow-by-default (WEB-0083), and <b>defers</b> for any other action so the seam's
/// general default or higher rungs decide.
/// </summary>
public sealed class PolicyAuthorizationProvider : IAuthorizationProvider
{
    private static readonly HashSet<string> CapabilityActionSet = BuildActionSet();

    private readonly CapabilityAuthorizationOptions _options;
    private readonly IAuthorizationService _authz;

    // AddCapabilityAuthorization registers the raw options instance (not IOptions<>), matching the engine
    // this provider absorbs.
    public PolicyAuthorizationProvider(CapabilityAuthorizationOptions options, IAuthorizationService authz)
    {
        _options = options;
        _authz = authz;
    }

    public int Order => 100;

    public async Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        if (!CapabilityActionSet.Contains(request.Action))
            return null; // not a capability action → defer to the seam default / higher rungs

        var policyName = ResolvePolicyName(request);
        if (policyName is null)
            // recognized capability action, no policy mapped → DefaultBehavior (definitive)
            return _options.DefaultBehavior == CapabilityDefaultBehavior.Allow
                ? AuthorizeDecision.Allowed()
                : AuthorizeDecision.Forbidden(
                    $"Capability '{request.Action}' on '{EntityName(request)}' has no policy, so it is denied. Map one in " +
                    $"AddCapabilityAuthorization: caps.Defaults.{request.Action} = \"a-policy\", or per entity through caps.Entities.");

        // Match CapabilityAuthorizer: evaluate the named policy with a null resource.
        var result = await _authz.AuthorizeAsync(request.Subject, resource: null, policyName).ConfigureAwait(false);
        return result.Succeeded
            ? AuthorizeDecision.Allowed()
            : AuthorizeDecision.Forbidden($"policy '{policyName}' denied");
    }

    private string? ResolvePolicyName(AuthorizeRequest request)
    {
        var entityName = EntityName(request);

        if (entityName is not null && _options.Entities.TryGetValue(entityName, out var entityPolicy))
        {
            var p = Pick(entityPolicy, request.Action);
            if (!string.IsNullOrWhiteSpace(p)) return p;
        }

        var d = Pick(_options.Defaults, request.Action);
        return string.IsNullOrWhiteSpace(d) ? null : d;
    }

    // The entity is conveyed as the request Resource (a Type, or an instance whose type names the entity).
    private static string? EntityName(AuthorizeRequest request)
        => (request.Resource as Type)?.Name ?? request.Resource?.GetType().Name;

    private static string? Pick(CapabilityPolicy policy, string action) => action switch
    {
        CapabilityActions.Moderation.DraftCreate => policy.Moderation.DraftCreate,
        CapabilityActions.Moderation.DraftUpdate => policy.Moderation.DraftUpdate,
        CapabilityActions.Moderation.DraftGet => policy.Moderation.DraftGet,
        CapabilityActions.Moderation.Submit => policy.Moderation.Submit,
        CapabilityActions.Moderation.Withdraw => policy.Moderation.Withdraw,
        CapabilityActions.Moderation.Queue => policy.Moderation.Queue,
        CapabilityActions.Moderation.Approve => policy.Moderation.Approve,
        CapabilityActions.Moderation.Reject => policy.Moderation.Reject,
        CapabilityActions.Moderation.Return => policy.Moderation.Return,

        CapabilityActions.Audit.Snapshot => policy.Audit.Snapshot,
        CapabilityActions.Audit.List => policy.Audit.List,
        CapabilityActions.Audit.Revert => policy.Audit.Revert,

        _ => null
    };

    private static HashSet<string> BuildActionSet()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nested in typeof(CapabilityActions).GetNestedTypes())
            foreach (var field in nested.GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.IsLiteral && field.FieldType == typeof(string) && field.GetRawConstantValue() is string value)
                    set.Add(value);
        return set;
    }
}
