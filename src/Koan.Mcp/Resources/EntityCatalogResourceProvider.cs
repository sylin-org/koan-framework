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

        var options = _options.Value;
        var entities = new JArray();

        foreach (var registration in _registry.Registrations)
        {
            // null principal = local-trust (STDIO, AN3): no projection constraint. A concrete principal
            // (always supplied by the remote edge, anonymous = an empty principal) is projected per grant.
            var verbs = registration.Tools
                .Where(tool => user is null || McpToolAccessPolicy.IsEntityToolPermitted(user, registration, tool, options))
                .Select(tool => new JObject
                {
                    ["name"] = tool.Name,
                    ["operation"] = tool.Operation.ToString(),
                    ["isMutation"] = tool.IsMutation
                })
                .ToList();

            // Walled-means-silent: an entity with no caller-visible verb is absent from the catalog.
            if (verbs.Count == 0)
            {
                continue;
            }

            entities.Add(new JObject
            {
                ["name"] = registration.DisplayName,
                ["description"] = registration.Attribute.Description,
                ["verbs"] = new JArray(verbs)
            });
        }

        var document = new JObject { ["entities"] = entities };
        return new McpResourceContents(ResourceUri, "application/json", document.ToString(Formatting.None));
    }
}
