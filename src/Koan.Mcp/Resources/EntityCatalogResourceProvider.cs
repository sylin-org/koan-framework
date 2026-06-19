using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Koan.Mcp.Execution;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Resources;

/// <summary>
/// P1.2 — the built-in <c>koan://entities</c> introspection resource: the catalog of MCP-projected
/// entities and their verbs, PROJECTED PER GRANT. Each entity's verbs are filtered through the shared
/// <see cref="McpToolAccessPolicy"/> (AN3) for the calling principal, and an entity with no caller-visible
/// verb is omitted entirely (walled-means-silent) — the same projection the tool surface enforces, now
/// readable as a resource rather than inferred from the tool list.
/// </summary>
public sealed class EntityCatalogResourceProvider : IMcpResourceProvider
{
    public const string ResourceUri = "koan://entities";

    private readonly McpEntityRegistry _registry;
    private readonly IOptions<McpServerOptions> _options;

    public EntityCatalogResourceProvider(McpEntityRegistry registry, IOptions<McpServerOptions> options)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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
        foreach (var (registration, verbs) in EntityProjection.Visible(_registry, _options.Value, user))
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
                }))
            });
        }

        var document = new JObject { ["entities"] = entities };
        return new McpResourceContents(ResourceUri, "application/json", document.ToString(Formatting.None));
    }
}
