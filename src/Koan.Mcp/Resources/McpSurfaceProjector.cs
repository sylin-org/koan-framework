using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Koan.Core;
using Koan.Mcp.CustomTools;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Koan.Web.Authorization;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Resources;

/// <summary>
/// WEB-0072 — the single per-caller surface projection that backs the MCP Explorer console and the
/// <c>{baseRoute}/map.json</c> endpoint. It mirrors exactly what the caller's MCP client sees (the same
/// <see cref="EntityProjection.Visible"/> gate the resources path uses), enriched with each tool's
/// description, input schema, and annotations so a human console can render an invocation form.
/// <para>
/// Three-state honesty (SEC-0004/0005): callable <c>tools</c> appear, denied-but-<c>[Door]</c> verbs are
/// disclosed under <c>doors</c> with their unlock <c>needs</c>, and Walls (role-gated or un-doored denials)
/// leave no trace. The result is <b>anonymous-safe by construction</b> — it discloses nothing the protocol
/// does not already hand that caller. Pass a real anonymous <see cref="ClaimsPrincipal"/> on the web path,
/// never <c>null</c> (null is STDIO local-trust and would suppress the Door disclosure).
/// </para>
/// </summary>
public sealed class McpSurfaceProjector
{
    private readonly McpEntityRegistry _registry;
    private readonly McpCustomToolRegistry _customTools;
    private readonly IAccessGateCache _gateCache;
    private readonly IOptions<McpServerOptions> _serverOptions;

    public McpSurfaceProjector(McpEntityRegistry registry, McpCustomToolRegistry customTools, IAccessGateCache gateCache, IOptions<McpServerOptions> serverOptions)
    {
        _registry = registry;
        _customTools = customTools;
        _gateCache = gateCache;
        _serverOptions = serverOptions;
    }

    /// <summary>Project the per-caller surface document for <paramref name="user"/>.</summary>
    public JObject Project(ClaimsPrincipal? user)
    {
        var app = KoanEnv.CurrentSnapshot.Application;

        var entities = new JArray();
        foreach (var (registration, verbs, doors) in EntityProjection.Visible(_registry, _gateCache, user))
        {
            var tools = new JArray();
            foreach (var verb in verbs)
            {
                tools.Add(ToolJson(McpRpcHandler.ToolDescriptor.From(registration, verb)));
            }

            var entity = new JObject
            {
                ["name"] = registration.DisplayName,
                ["description"] = registration.Attribute.Description,
                ["tools"] = tools,
            };

            // SEC-0005 Door: denied-but-disclosed verbs with how to unlock them. Omitted entirely when empty —
            // a Wall leaves no trace (privilege non-enumeration).
            if (doors.Count > 0)
            {
                entity["doors"] = new JArray(doors.Select(door => new JObject
                {
                    ["name"] = door.Tool.Name,
                    ["operation"] = door.Tool.Operation.ToString(),
                    ["needs"] = door.Needs,
                }));
            }

            entities.Add(entity);
        }

        var customTools = new JArray();
        foreach (var tool in CustomToolProjection.Visible(_customTools, _serverOptions.Value, user))
        {
            customTools.Add(ToolJson(McpRpcHandler.ToolDescriptor.FromCustom(tool)));
        }

        var options = _serverOptions.Value;
        var instructions = !string.IsNullOrWhiteSpace(options.Instructions)
            ? options.Instructions
            : (string.IsNullOrWhiteSpace(app.Description) ? null : app.Description);

        return new JObject
        {
            ["identity"] = new JObject
            {
                ["name"] = app.Name,
                ["code"] = app.Code,
                ["description"] = app.Description,
                ["contactEmail"] = app.ContactEmail,
                ["supportUrl"] = app.SupportUrl,
                ["tags"] = new JArray(app.Tags),
            },
            // WEB-0072 P2: the LLM-facing guidance the initialize handshake returns — surfaced so the console can
            // show "what the agent is told" (and a developer can tune it).
            ["instructions"] = instructions,
            ["transport"] = new JObject
            {
                ["stdio"] = options.EnableStdioTransport,
                ["streamableHttp"] = options.EnableStreamableHttpTransport,
                ["legacySse"] = options.EnableLegacySseTransport,
                ["httpRoute"] = string.IsNullOrWhiteSpace(options.HttpRoute) ? "/mcp" : options.HttpRoute.TrimEnd('/'),
            },
            ["entities"] = entities,
            ["customTools"] = customTools,
        };
    }

    // DeepClone the schema/annotation JObjects: the registry shares one instance per tool, and a JToken may not
    // be parented twice — cloning keeps the registry's structure intact while we compose the document.
    private static JObject ToolJson(McpRpcHandler.ToolDescriptor descriptor)
    {
        var o = new JObject
        {
            ["name"] = descriptor.Name,
            ["description"] = descriptor.Description,
            ["inputSchema"] = descriptor.InputSchema?.DeepClone(),
            ["annotations"] = descriptor.Annotations?.DeepClone(),
            ["metadata"] = descriptor.Metadata?.DeepClone(),
        };
        if (descriptor.OutputSchema is not null) o["outputSchema"] = descriptor.OutputSchema.DeepClone();
        return o;
    }
}
