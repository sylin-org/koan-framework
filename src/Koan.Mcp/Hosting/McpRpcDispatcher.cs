using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.Execution;
using Koan.Mcp.Options;
using Koan.Mcp.Resources;
using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Hosting;

/// <summary>
/// AI-0037 — the transport-agnostic MCP JSON-RPC dispatch core. Owns the method switch
/// (<c>initialize</c> / <c>tools/list</c> / <c>tools/call</c> / <c>resources/*</c> / <c>ping</c>) AND the
/// SEC-0004 visibility + access gating, producing a JSON-RPC response <see cref="JObject"/> (or <c>null</c> for a
/// method that yields no response) from a request envelope + the caller principal. Every HTTP surface routes through
/// here so the security-sensitive logic cannot drift between transports — the "one projection or it drifts" discipline
/// (SEC-0004) applied to the transport edge.
/// </summary>
public sealed class McpRpcDispatcher
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    private readonly McpEntityRegistry _registry;
    private readonly Koan.Mcp.CustomTools.McpCustomToolRegistry _customTools;
    private readonly IAccessGateCache _gateCache;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly ILogger<McpRpcDispatcher> _logger;

    public McpRpcDispatcher(
        McpEntityRegistry registry,
        Koan.Mcp.CustomTools.McpCustomToolRegistry customTools,
        IAccessGateCache gateCache,
        IOptionsMonitor<McpServerOptions> options,
        ILogger<McpRpcDispatcher> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _customTools = customTools ?? throw new ArgumentNullException(nameof(customTools));
        _gateCache = gateCache ?? throw new ArgumentNullException(nameof(gateCache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Dispatch one JSON-RPC request envelope under <paramref name="principal"/> (never null at a remote edge — an
    /// anonymous caller is a concrete empty principal, never STDIO local-trust) using a per-request
    /// <paramref name="handler"/>. Returns the JSON-RPC response object; the caller routes it to its transport.
    /// </summary>
    public async Task<JObject?> DispatchAsync(JsonRpcEnvelope envelope, ClaimsPrincipal principal, McpRpcHandler handler, CancellationToken cancellationToken)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        principal ??= new ClaimsPrincipal();

        if (string.IsNullOrWhiteSpace(envelope.Method))
        {
            return CreateError(envelope.Id, -32600, "Missing method.");
        }

        try
        {
            switch (envelope.Method)
            {
                case "initialize":
                    return await HandleInitialize(envelope, handler, cancellationToken);
                case "tools/list":
                    return await HandleToolsList(envelope, principal, handler, cancellationToken);
                case "tools/call":
                    return await HandleToolsCall(envelope, principal, handler, cancellationToken);
                case "resources/list":
                    return HandleResourcesList(envelope, principal, handler);
                case "resources/read":
                    return HandleResourcesRead(envelope, principal, handler);
                case "ping":
                    return Wrap(envelope.Id, new JObject());
                default:
                    return CreateError(envelope.Id, -32601, $"Method '{envelope.Method}' is not supported.");
            }
        }
        catch (OperationCanceledException)
        {
            return CreateError(envelope.Id, -32603, "Operation cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling JSON-RPC request {Method}.", envelope.Method);
            return CreateError(envelope.Id, -32603, "Internal server error.");
        }
    }

    private async Task<JObject> HandleInitialize(JsonRpcEnvelope envelope, McpRpcHandler handler, CancellationToken cancellationToken)
    {
        var parameters = envelope.Params is JObject obj
            ? obj.ToObject<McpRpcHandler.InitializeParams>(JsonSerializer.Create(SerializerSettings))
            : null;
        var response = await handler.Initialize(parameters, cancellationToken);
        return Wrap(envelope.Id, JToken.FromObject(response, JsonSerializer.Create(SerializerSettings)));
    }

    private async Task<JObject> HandleToolsList(JsonRpcEnvelope envelope, ClaimsPrincipal principal, McpRpcHandler handler, CancellationToken cancellationToken)
    {
        var response = await handler.ListExposedTools(cancellationToken);
        using var visibility = McpVerbVisibility.For(handler.Services.GetService<IServiceScopeFactory>(), _gateCache, principal);
        var filtered = new List<McpRpcHandler.ToolDescriptor>(response.Tools.Count);
        foreach (var tool in response.Tools)
        {
            if (await IsToolVisible(tool, principal, visibility, cancellationToken)) filtered.Add(tool);
        }
        if (filtered.Count != response.Tools.Count)
        {
            response = new McpRpcHandler.ToolsListResponse { Tools = filtered.ToArray() };
        }
        return Wrap(envelope.Id, JToken.FromObject(response, JsonSerializer.Create(SerializerSettings)));
    }

    private async Task<JObject> HandleToolsCall(JsonRpcEnvelope envelope, ClaimsPrincipal principal, McpRpcHandler handler, CancellationToken cancellationToken)
    {
        if (envelope.Params is not JObject parameters)
        {
            return CreateError(envelope.Id, -32602, "Expected params object.");
        }

        if (!parameters.TryGetValue("name", StringComparison.OrdinalIgnoreCase, out var nameNode) || nameNode?.Value<string>() is not { Length: > 0 } toolName)
        {
            return CreateError(envelope.Id, -32602, "Missing tool name.");
        }

        if (!_registry.TryGetTool(toolName, out _, out _) && !_customTools.TryGet(toolName, out _))
        {
            return CreateError(envelope.Id, -32602, $"Tool '{toolName}' is not registered.",
                Koan.Mcp.CustomTools.ToolAnswers.Json(new Koan.Web.Controllers.Problem(Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound, $"No tool here is named '{toolName}'.")));
        }

        // SEC-0004, ARCH-0141: a tool's authority is the one gate, enforced inside CallToolFor with the threaded
        // principal, so a denial is a failed call and not a transport-edge error.
        JObject? arguments = null;
        if (parameters.TryGetValue("arguments", StringComparison.OrdinalIgnoreCase, out var argsNode) && argsNode is JObject argsObj)
        {
            arguments = argsObj;
        }

        var result = await handler.CallToolFor(new McpRpcHandler.ToolsCallParams { Name = toolName, Arguments = arguments }, principal, cancellationToken);
        return Wrap(envelope.Id, JToken.FromObject(result, JsonSerializer.Create(SerializerSettings)));
    }

    // P1.2: resources are PROJECTED PER GRANT inside the provider — pass the caller principal so List/Read reflect
    // only what this caller may see (no separate visibility filter needed).
    private JObject HandleResourcesList(JsonRpcEnvelope envelope, ClaimsPrincipal principal, McpRpcHandler handler)
        => Wrap(envelope.Id, JToken.FromObject(handler.ListResourcesFor(principal), JsonSerializer.Create(SerializerSettings)));

    private JObject HandleResourcesRead(JsonRpcEnvelope envelope, ClaimsPrincipal principal, McpRpcHandler handler)
    {
        if (envelope.Params is not JObject parameters
            || !parameters.TryGetValue("uri", StringComparison.OrdinalIgnoreCase, out var uriNode)
            || uriNode?.Value<string>() is not { Length: > 0 } uri)
        {
            return CreateError(envelope.Id, -32602, "Missing 'uri' parameter.");
        }

        return Wrap(envelope.Id, JToken.FromObject(handler.ReadResourceFor(uri, principal), JsonSerializer.Create(SerializerSettings)));
    }

    // SEC-0011: tools/list must not advertise what the call will deny. A tool is visible iff the authorization ladder its
    // call enforces (grants, added providers, the default decision) allows the principal, which for a custom verb is asked
    // with no resource at hand (ARCH-0141). The principal is never null at a remote edge.
    private async Task<bool> IsToolVisible(McpRpcHandler.ToolDescriptor tool, ClaimsPrincipal principal,
        McpVerbVisibility visibility, CancellationToken cancellationToken)
    {
        if (_registry.TryGetTool(tool.Name, out var registration, out var definition))
        {
            return await visibility.Allows(registration.EntityType, definition.Operation, cancellationToken);
        }

        if (_customTools.TryGet(tool.Name, out var custom))
        {
            return await CustomToolProjection.IsVisible(custom, _options.CurrentValue, visibility, cancellationToken);
        }

        return false;
    }

    private JObject Wrap(JToken? id, JToken? result)
        => result is null
            ? CreateError(id, -32603, "Failed to serialise response.")
            : new JObject { ["jsonrpc"] = "2.0", ["id"] = CloneId(id), ["result"] = result };

    /// <summary>Build a JSON-RPC error response object (shared by every transport), whose <c>data</c> is the
    /// <see cref="Koan.Web.Controllers.Problem"/> every failure at the edge carries (AI-0042), the one given or the code's own.</summary>
    public static JObject CreateError(JToken? id, int code, string message, JToken? data = null)
    {
        var status = code switch
        {
            -32700 or -32600 or -32602 => Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest,
            -32601 => Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound,
            _ => Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError,
        };
        var error = new JObject { ["code"] = code, ["message"] = message, ["data"] = data ?? Koan.Mcp.CustomTools.ToolAnswers.Json(new Koan.Web.Controllers.Problem(status, message)) };
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = CloneId(id), ["error"] = error };
    }

    private static JToken? CloneId(JToken? id) => id?.DeepClone();
}
