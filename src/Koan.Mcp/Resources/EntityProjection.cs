using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Koan.Mcp.Execution;
using Koan.Web.Authorization;

namespace Koan.Mcp.Resources;

/// <summary>
/// P1.2/AN8 — the shared per-grant entity projection used by both the <c>koan://entities</c> catalog and
/// the <c>koan://self</c> self-introduction. For the caller, returns each MCP-projected entity paired with
/// only the verbs that caller may invoke — coarsely evaluated against the SAME data-layer <c>[Access]</c> gate
/// the endpoint service enforces (via <see cref="McpEntityGate"/>), so the catalog never advertises what a call
/// would deny. An entity with no caller-visible verb is omitted (walled-means-silent). A null principal = STDIO
/// local-trust (full); a concrete (even anonymous) principal is gated per verb.
/// </summary>
internal static class EntityProjection
{
    public static IReadOnlyList<(McpEntityRegistration Registration, IReadOnlyList<McpToolDefinition> Verbs)> Visible(
        McpEntityRegistry registry, IAccessGateCache gateCache, ClaimsPrincipal? user)
    {
        var result = new List<(McpEntityRegistration, IReadOnlyList<McpToolDefinition>)>();
        foreach (var registration in registry.Registrations)
        {
            var verbs = registration.Tools
                .Where(tool => McpEntityGate.CoarseAllows(gateCache, registration.EntityType, tool.Operation, user))
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
