using System;
using System.Linq;
using Koan.Core;
using Koan.Mcp.CustomTools;
using Koan.Web.Authorization;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Resources;

/// <summary>
/// WEB-0072 D5 — the PRIVILEGED, caller-independent "access map": every capability mapped to its EXACT access
/// requirement, <b>walls included</b>. It is the un-redacted inverse of <see cref="McpSurfaceProjector"/> (which
/// is per-caller and hides walls), so it must never be served anonymously.
/// <para>
/// Derived from the same compiled gate the floor enforces (<see cref="IAccessGateCache"/> →
/// <see cref="AccessGateEvaluator.Describe"/>), so it cannot drift from enforcement — which is what makes it
/// trustworthy as an audit artifact. Uses <c>Describe</c> (un-redacted), <b>not</b> the runtime-redacted
/// <c>McpEntityGate.DoorNeeds</c> (which hides role gates).
/// </para>
/// </summary>
public sealed class AccessMapProjector
{
    private static readonly string[] Actions = { "read", "write", "remove" };

    private readonly McpEntityRegistry _registry;
    private readonly McpCustomToolRegistry _customTools;
    private readonly IAccessGateCache _gateCache;

    public AccessMapProjector(McpEntityRegistry registry, McpCustomToolRegistry customTools, IAccessGateCache gateCache)
    {
        _registry = registry;
        _customTools = customTools;
        _gateCache = gateCache;
    }

    public JObject Project()
    {
        var app = KoanEnv.CurrentSnapshot.Application;

        var entities = new JArray();
        foreach (var registration in _registry.Registrations.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var gate = _gateCache.GetOrCompile(registration.EntityType);
            var access = new JObject();
            foreach (var action in Actions)
            {
                var actionGate = gate.For(action);
                access[action] = actionGate.IsOpen ? "anonymous" : Normalize(AccessGateEvaluator.Describe(actionGate));
            }

            entities.Add(new JObject
            {
                ["name"] = registration.DisplayName,
                ["description"] = registration.Attribute.Description,
                ["source"] = "entity",
                ["access"] = access,
            });
        }

        var customTools = new JArray();
        foreach (var tool in _customTools.Tools.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            customTools.Add(new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["source"] = "custom",
                ["requirement"] = DescribeCustom(tool),
            });
        }

        return new JObject
        {
            ["identity"] = new JObject
            {
                ["name"] = app.Name,
                ["code"] = app.Code,
                ["description"] = app.Description,
            },
            ["entities"] = entities,
            ["customTools"] = customTools,
        };
    }

    // A custom verb answers to the same gate as an entity, read off its own declarations (ARCH-0141).
    private string DescribeCustom(McpCustomTool tool)
    {
        var gate = _gateCache.GetOrCompile(tool.Method).For(tool.GateAction);
        return gate.IsOpen ? "anonymous" : Normalize(AccessGateEvaluator.Describe(gate));
    }

    // Render the origin claim grant the way the mental model reads (claim:koan:origin=local -> origin:local).
    private static string Normalize(string described)
        => described.Replace("claim:koan:origin=", "origin:");
}
