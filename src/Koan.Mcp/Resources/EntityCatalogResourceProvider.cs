using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Koan.Web.Authorization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Resources;

/// <summary>
/// P1.2 — the built-in <c>koan://entities</c> introspection resource: the catalog of MCP-projected
/// entities and their verbs, PROJECTED PER GRANT. Each entity's verbs are filtered through the SAME
/// data-layer <c>[Access]</c> gate the endpoint service enforces (via <see cref="Koan.Mcp.Execution.McpEntityGate"/>),
/// and an entity with no caller-visible verb is omitted entirely (walled-means-silent) — the same projection the
/// tool surface enforces, now readable as a resource rather than inferred from the tool list.
/// </summary>
public sealed class EntityCatalogResourceProvider : IMcpResourceProvider
{
    public const string ResourceUri = "koan://entities";

    private readonly McpEntityRegistry _registry;
    private readonly Koan.Data.Core.Relationships.IRelationshipMetadata _metadata;
    private readonly IAccessGateCache _gateCache;

    public EntityCatalogResourceProvider(
        McpEntityRegistry registry,
        Koan.Data.Core.Relationships.IRelationshipMetadata metadata,
        IAccessGateCache gateCache)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _gateCache = gateCache ?? throw new ArgumentNullException(nameof(gateCache));
    }

    public IEnumerable<McpResourceDescriptor> List(ClaimsPrincipal? user)
    {
        yield return new McpResourceDescriptor(
            ResourceUri,
            "Entity catalog",
            "The entities this application projects over MCP and the verbs available to you.",
            "application/json");
    }

    public McpResourceContents? Read(string uri, ClaimsPrincipal? user)
    {
        if (!string.Equals(uri, ResourceUri, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var entities = new JArray();

        // Shared per-grant projection (null principal = local-trust full; concrete principal = per grant).
        var visible = EntityProjection.Visible(_registry, _gateCache, user);

        // AN7: edges are governed at the catalog level by target-type visibility — an edge pointing at a
        // type this grant cannot see is absent (walled-means-silent). Build the visible-type → name map once.
        var visibleByType = new Dictionary<Type, string>();
        foreach (var (registration, _) in visible)
        {
            visibleByType[registration.EntityType] = registration.DisplayName;
        }

        foreach (var (registration, verbs) in visible)
        {
            entities.Add(new JObject
            {
                ["name"] = registration.DisplayName,
                ["description"] = registration.Attribute.Description,
                ["verbs"] = new JArray(verbs.Select(tool => new JObject
                {
                    ["name"] = tool.Name,
                    ["operation"] = tool.Operation.ToString(),
                    ["isMutation"] = tool.IsMutation
                })),
                // AN7: the navigable graph — edges as routes (target + via field), never verbs.
                ["edges"] = EntityEdgeProjection.For(registration.EntityType, _metadata, visibleByType)
            });
        }

        var document = new JObject { ["entities"] = entities };
        return new McpResourceContents(ResourceUri, "application/json", document.ToString(Formatting.None));
    }
}
