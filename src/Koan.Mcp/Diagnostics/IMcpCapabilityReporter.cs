using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Mcp;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Diagnostics;

public interface IMcpCapabilityReporter
{
    Task<McpCapabilityDocument> GetCapabilitiesAsync(CancellationToken cancellationToken);
}

public sealed class HttpSseCapabilityReporter : IMcpCapabilityReporter
{
    private readonly McpEntityRegistry _registry;
    private readonly IOptionsMonitor<McpServerOptions> _options;

    public HttpSseCapabilityReporter(McpEntityRegistry registry, IOptionsMonitor<McpServerOptions> options)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<McpCapabilityDocument> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        var transports = new[]
        {
            new McpTransportDescription
            {
                Kind = "http+sse",
                StreamEndpoint = Combine(options.HttpSseRoute, "sse"),
                SubmitEndpoint = Combine(options.HttpSseRoute, "rpc"),
                CapabilityEndpoint = options.PublishCapabilityEndpoint ? Combine(options.HttpSseRoute, "capabilities") : null,
                RequireAuthentication = options.RequireAuthentication
            }
        };

        var tools = _registry.Registrations
            .SelectMany(registration => registration.Tools.Select(tool => new McpCapabilityTool
            {
                Name = tool.Name,
                Description = tool.Description,
                RequireAuthentication = registration.RequireAuthentication ?? options.RequireAuthentication,
                EnabledTransports = registration.EnabledTransports
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

    private static string Combine(string? route, string segment)
    {
        var prefix = string.IsNullOrWhiteSpace(route) ? "/mcp" : route.TrimEnd('/');
        return $"{prefix}/{segment}";
    }
}

public sealed record McpCapabilityDocument
{
    public string Version { get; init; } = "2.0";
    public IReadOnlyList<McpTransportDescription> Transports { get; init; } = Array.Empty<McpTransportDescription>();
    public IReadOnlyList<McpCapabilityTool> Tools { get; init; } = Array.Empty<McpCapabilityTool>();
}

public sealed record McpTransportDescription
{
    public required string Kind { get; init; }
    public required string StreamEndpoint { get; init; }
    public required string SubmitEndpoint { get; init; }
    public string? CapabilityEndpoint { get; init; }
    public bool RequireAuthentication { get; init; }
}

public sealed record McpCapabilityTool
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool RequireAuthentication { get; init; }
    public McpTransportMode EnabledTransports { get; init; } = McpTransportMode.All;
}
