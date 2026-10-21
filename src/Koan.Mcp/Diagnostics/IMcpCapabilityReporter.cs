using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Mcp;
using Koan.Mcp.Execution;
using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Diagnostics;

public interface IMcpCapabilityReporter
{
    /// <summary>The host's MCP edge with every registered entity tool, unfiltered: an operator's view.</summary>
    Task<McpCapabilityDocument> GetCapabilities(CancellationToken cancellationToken);

    /// <summary>
    /// SEC-0018: the document as <paramref name="caller"/> may see it, the one the capabilities endpoint serves. A
    /// reporter that cannot filter lists no tools.
    /// </summary>
    async Task<McpCapabilityDocument> GetCapabilitiesFor(ClaimsPrincipal caller, CancellationToken cancellationToken)
        => (await GetCapabilities(cancellationToken).ConfigureAwait(false)) with { Tools = [] };
}

public sealed class McpCapabilityReporter : IMcpCapabilityReporter
{
    private readonly McpEntityRegistry _registry;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly IServiceScopeFactory? _scopes;
    private readonly IAccessGateCache? _gates;

    /// <summary>SEC-0018: tools are listed per caller by the decision the calls enforce (SEC-0011).</summary>
    public McpCapabilityReporter(McpEntityRegistry registry, IOptionsMonitor<McpServerOptions> options,
        IServiceScopeFactory scopes, IAccessGateCache gates)
        : this(registry, options)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _gates = gates ?? throw new ArgumentNullException(nameof(gates));
    }

    public McpCapabilityReporter(McpEntityRegistry registry, IOptionsMonitor<McpServerOptions> options)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<McpCapabilityDocument> GetCapabilitiesFor(ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var document = await GetCapabilities(cancellationToken).ConfigureAwait(false);
        if (_gates is null || !McpExposure.ExposesTools(McpExposure.Resolve(_options.CurrentValue)))
            return document with { Tools = [] };

        using var visibility = McpVerbVisibility.For(_scopes, _gates, caller);
        var visible = new List<McpCapabilityTool>(document.Tools.Count);
        foreach (var tool in document.Tools)
        {
            if (_registry.TryGetTool(tool.Name, out var registration, out var definition)
                && await visibility.Allows(registration.EntityType, definition.Operation, cancellationToken).ConfigureAwait(false))
                visible.Add(tool);
        }
        return document with { Tools = visible };
    }

    public Task<McpCapabilityDocument> GetCapabilities(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        var route = Normalize(options.HttpRoute);
        var transports = new List<McpTransportDescription>();
        if (options.EnableStreamableHttpTransport)
        {
            transports.Add(new McpTransportDescription
            {
                Kind = "streamable-http",
                Endpoint = route,
                CapabilityEndpoint = options.PublishCapabilityEndpoint ? Combine(route, "capabilities") : null,
                RequireAuthentication = options.RequireAuthentication
            });
        }
        if (options.EnableLegacySseTransport)
        {
            transports.Add(new McpTransportDescription
            {
                Kind = "legacy-sse",
                StreamEndpoint = Combine(route, "sse"),
                SubmitEndpoint = Combine(route, "rpc"),
                CapabilityEndpoint = options.PublishCapabilityEndpoint ? Combine(route, "capabilities") : null,
                RequireAuthentication = options.RequireAuthentication
            });
        }

        var tools = _registry.Registrations
            .SelectMany(registration => registration.Tools.Select(tool => new McpCapabilityTool
            {
                Name = tool.Name,
                Description = tool.Description,
                // SEC-0004 Phase 3.3b: entity tools no longer carry a per-entity auth flag; this reports the
                // server-wide transport authentication requirement. Per-entity access is the [Access] gate.
                RequireAuthentication = options.RequireAuthentication
            }))
            .ToArray();

        var document = new McpCapabilityDocument
        {
            Version = "2.0",
            Transports = transports,
            Tools = tools
        };

        return Task.FromResult(document);
    }

    private static string Normalize(string? route)
    {
        var normalized = string.IsNullOrWhiteSpace(route) ? "/mcp" : route.TrimEnd('/');
        return string.IsNullOrEmpty(normalized) ? "/mcp" : normalized;
    }

    private static string Combine(string route, string segment) => $"{route}/{segment}";
}

public sealed record McpCapabilityDocument
{
    public string Version { get; init; } = "2.0";
    public IReadOnlyList<McpTransportDescription> Transports { get; init; } = [];
    public IReadOnlyList<McpCapabilityTool> Tools { get; init; } = [];
}

public sealed record McpTransportDescription
{
    public required string Kind { get; init; }
    public string? Endpoint { get; init; }
    public string? StreamEndpoint { get; init; }
    public string? SubmitEndpoint { get; init; }
    public string? CapabilityEndpoint { get; init; }
    public bool RequireAuthentication { get; init; }
}

public sealed record McpCapabilityTool
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool RequireAuthentication { get; init; }
}
