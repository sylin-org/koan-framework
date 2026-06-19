using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Koan.Mcp.Execution;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;

namespace Koan.Mcp.Resources;

/// <summary>
/// P1.2/AN8 — the shared per-grant entity projection used by both the <c>koan://entities</c> catalog and
/// the <c>koan://self</c> self-introduction. For the caller, returns each MCP-projected entity paired with
/// only the verbs that caller may invoke (via the shared <see cref="McpToolAccessPolicy"/>); an entity with
/// no caller-visible verb is omitted (walled-means-silent). A null principal = STDIO local-trust (full).
/// </summary>
internal static class EntityProjection
{
    public static IReadOnlyList<(McpEntityRegistration Registration, IReadOnlyList<McpToolDefinition> Verbs)> Visible(
        McpEntityRegistry registry, McpServerOptions options, ClaimsPrincipal? user)
    {
        var result = new List<(McpEntityRegistration, IReadOnlyList<McpToolDefinition>)>();
        foreach (var registration in registry.Registrations)
        {
            var verbs = registration.Tools
                .Where(tool => user is null || McpToolAccessPolicy.IsEntityToolPermitted(user, registration, tool, options))
                .ToList();

            if (verbs.Count == 0)
            {
                continue;
            }

            result.Add((registration, verbs));
        }

        return result;
    }
}
