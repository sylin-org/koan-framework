using System;
using System.Collections.Generic;

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
    public string? SchemaOverride { get; set; }
    public string[] RequiredScopes { get; set; } = Array.Empty<string>();
}
