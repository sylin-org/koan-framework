using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Koan.Mcp.Options;

namespace Koan.Mcp.Execution;

/// <summary>
/// AN3 (docs/assessment/09 §8) — the single MCP effective-access decision: <c>requiresAuth ∩ required
/// scopes</c> evaluated against the caller's <see cref="ClaimsPrincipal"/>. Every remote transport edge
/// consults THIS policy so enforcement is one projection, not a per-transport copy that can drift (the
/// WEB-0068 per-surface-drift lesson applied to MCP authz).
///
/// Transport trust model:
/// <list type="bullet">
///   <item><b>HTTP/SSE</b> (remote) — <see cref="Koan.Mcp.Hosting.HttpSseRpcBridge"/> gates both
///   <c>tools/list</c> (filter) and <c>tools/call</c> (deny) through this policy with the authenticated
///   session principal.</item>
///   <item><b>STDIO</b> (local) — binds the raw <see cref="Koan.Mcp.Hosting.McpRpcHandler"/> with no
///   filter: stdin/stdout is the same-machine process owner, so it is full local-trust BY DESIGN (the
///   handler is unfiltered; enforcement is a transport-edge concern). Any FUTURE remote transport MUST
///   wrap the handler with this policy — that local-trust invariant is pinned by a tripwire test.</item>
/// </list>
/// </summary>
public static class McpToolAccessPolicy
{
    private static readonly string[] ScopeClaimTypes =
    {
        "scope",
        "scp",
        "http://schemas.microsoft.com/identity/claims/scope"
    };

    /// <summary>The core decision: a caller is permitted when authentication (if required) is satisfied
    /// and the caller holds every required scope.</summary>
    public static bool IsPermitted(ClaimsPrincipal? user, bool requiresAuth, IReadOnlyList<string> requiredScopes)
    {
        if (requiresAuth && user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return requiredScopes is null or { Count: 0 } || UserHasScopes(user, requiredScopes);
    }

    /// <summary>Effective access for an entity tool: the registration's per-entity auth requirement
    /// (falling back to the server default) ∩ the tool's required scopes.</summary>
    public static bool IsEntityToolPermitted(ClaimsPrincipal? user, McpEntityRegistration registration, McpToolDefinition tool, McpServerOptions options)
    {
        if (registration is null) throw new ArgumentNullException(nameof(registration));
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (options is null) throw new ArgumentNullException(nameof(options));

        var requiresAuth = registration.RequireAuthentication ?? options.RequireAuthentication;
        return IsPermitted(user, requiresAuth, tool.RequiredScopes);
    }

    /// <summary>Effective access for a custom <c>[McpTool]</c> verb: the server auth requirement ∩ the
    /// verb's required scopes.</summary>
    public static bool IsCustomToolPermitted(ClaimsPrincipal? user, Koan.Mcp.CustomTools.McpCustomTool tool, McpServerOptions options)
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (options is null) throw new ArgumentNullException(nameof(options));

        return IsPermitted(user, options.RequireAuthentication, tool.RequiredScopes);
    }

    /// <summary>True when the caller presents every required scope (space-delimited <c>scope</c>/<c>scp</c>
    /// claim values across the common claim types).</summary>
    public static bool UserHasScopes(ClaimsPrincipal? user, IReadOnlyList<string> requiredScopes)
    {
        if (requiredScopes is null || requiredScopes.Count == 0)
        {
            return true;
        }

        if (user is null)
        {
            return false;
        }

        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claimType in ScopeClaimTypes)
        {
            foreach (var claim in user.FindAll(claimType))
            {
                if (string.IsNullOrWhiteSpace(claim.Value))
                {
                    continue;
                }

                foreach (var value in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    scopes.Add(value);
                }
            }
        }

        if (scopes.Count == 0)
        {
            return false;
        }

        return requiredScopes.All(scope => scopes.Contains(scope));
    }
}
