using System;
using System.Collections.Generic;
using Koan.Core;
using Koan.Mcp;

namespace Koan.Mcp.Options;

/// <summary>
/// Root configuration object bound from Koan:Mcp.*
/// </summary>
public sealed class McpServerOptions
{
    /// <summary>
    /// Controls whether the STDIO transport is hosted automatically.
    /// </summary>
    public bool EnableStdioTransport { get; set; } = true;

    /// <summary>
    /// Controls whether the HTTP + SSE transport is hosted automatically.
    /// </summary>
    public bool EnableHttpSseTransport { get; set; } = false;

    /// <summary>
    /// Base route used for HTTP + SSE endpoints (e.g. /mcp => /mcp/sse, /mcp/rpc).
    /// </summary>
    public string HttpSseRoute { get; set; } = "/mcp";

    private bool? _requireAuthentication;

    /// <summary>
    /// Indicates whether HTTP + SSE endpoints require authentication. Defaults to true in production or container environments.
    /// </summary>
    public bool RequireAuthentication
    {
        get => _requireAuthentication ?? (KoanEnv.IsProduction || KoanEnv.InContainer);
        set => _requireAuthentication = value;
    }

    /// <summary>
    /// Maximum concurrent HTTP + SSE sessions allowed.
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 100;

    /// <summary>
    /// Maximum idle duration before a session is reclaimed.
    /// </summary>
    public TimeSpan SseConnectionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Enables CORS for the HTTP + SSE transport.
    /// </summary>
    public bool EnableCors { get; set; } = false;

    /// <summary>
    /// Allowed origins when CORS is enabled.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Controls whether the discovery endpoint (/capabilities) is published.
    /// </summary>
    public bool PublishCapabilityEndpoint { get; set; } = true;

    /// <summary>
    /// When supplied only entities listed here are exposed (matched against full name or simple name).
    /// </summary>
    public ISet<string> AllowedEntities { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Entities listed here are filtered even if decorated with <see cref="McpEntityAttribute"/>.
    /// </summary>
    public ISet<string> DeniedEntities { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional per-entity overrides keyed by entity full name or attribute name.
    /// </summary>
    public Dictionary<string, McpEntityOverride> EntityOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Telemetry and heartbeat behaviour for transports.
    /// </summary>
    public McpTransportOptions Transport { get; set; } = new();
}

public sealed class McpEntityOverride
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? AllowMutations { get; set; }
    public bool? EnableStdio { get; set; }
    public bool? EnableHttpSse { get; set; }
    public bool? RequireAuthentication { get; set; }
    public McpTransportMode? EnabledTransports { get; set; }
    public string? SchemaOverride { get; set; }
    public string[] RequiredScopes { get; set; } = Array.Empty<string>();
}
