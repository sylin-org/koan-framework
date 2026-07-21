using System.Collections.Generic;
using System.Security.Claims;
using Koan.Mcp.Execution;
using Koan.Web.Authorization;

namespace Koan.Mcp.Resources;

/// <summary>
/// P1.2/AN8 + SEC-0005 (the Door) — the shared per-grant entity projection used by both the <c>koan://entities</c>
/// catalog and the <c>koan://self</c> self-introduction. Each MCP-projected entity is paired with the <b>verbs</b>
/// the caller may invoke (coarsely evaluated against the SAME data-layer <c>[Access]</c> gate the endpoint enforces,
/// via <see cref="McpEntityGate"/>) AND any <b>doors</b> — verbs the caller may NOT invoke but that a <c>[Door]</c>
/// entity discloses with their <c>needs</c> (how to unlock). A verb that is neither callable nor a door is a silent
/// Wall (absent). An entity with no verb AND no door is omitted entirely. A null principal = STDIO local-trust (full,
/// no doors).
/// </summary>
internal static class EntityProjection
{
    /// <summary>A disclosed-but-denied verb: the tool and the gate's <c>needs</c> (how to unlock it).</summary>
    public readonly record struct Door(McpToolDefinition Tool, string Needs);

    public static IReadOnlyList<(McpEntityRegistration Registration, IReadOnlyList<McpToolDefinition> Verbs, IReadOnlyList<Door> Doors)> Visible(
        McpEntityRegistry registry, IAccessGateCache gateCache, ClaimsPrincipal? user)
    {
        var result = new List<(McpEntityRegistration, IReadOnlyList<McpToolDefinition>, IReadOnlyList<Door>)>();
        foreach (var registration in registry.Registrations)
        {
            var verbs = new List<McpToolDefinition>();
            var doors = new List<Door>();
            foreach (var tool in registration.Tools)
            {
                if (McpEntityGate.CoarseAllows(gateCache, registration.EntityType, tool.Operation, user))
                {
                    verbs.Add(tool);
                }
                else if (McpEntityGate.DoorNeeds(gateCache, registration.EntityType, tool.Operation, user) is { } needs)
                {
                    doors.Add(new Door(tool, needs));
                }
            }

            // Walled-means-silent: omit only when there is neither a callable verb NOR a disclosed door.
            if (verbs.Count == 0 && doors.Count == 0)
            {
                continue;
            }

            result.Add((registration, verbs, doors));
        }

        return result;
    }
}
