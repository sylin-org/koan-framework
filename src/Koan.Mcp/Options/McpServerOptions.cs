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
    /// Controls whether the STDIO transport is hosted.
    /// </summary>
    /// <remarks>
    /// Hosting STDIO additionally requires the process to have been launched with standard output as a
    /// protocol channel (<c>KOAN_MCP_STDIO=1</c> or <c>--mcp-stdio</c>). That launch signal is the gate;
    /// this switch only narrows it, so the two can never disagree about who owns standard output.
    /// </remarks>
    public bool EnableStdioTransport { get; set; } = true;

    /// <summary>
    /// Controls whether the MCP Streamable HTTP transport is hosted. The transport is secure opt-in.
    /// </summary>
    public bool EnableStreamableHttpTransport { get; set; } = false;

    /// <summary>
    /// Base route for Streamable HTTP (for example, <c>/mcp</c>).
    /// </summary>
    public string HttpRoute { get; set; } = "/mcp";

    /// <summary>
    /// SEC-0039 — closes the HTTP door to anyone the application's own authentication did not find, who is then answered
    /// as that authentication answers anybody signed out. False, the default: the edge has no authentication of its own,
    /// and a caller with no credential is a visitor whom each tool's gate judges, as each route's does.
    /// </summary>
    public bool RequireAuthentication { get; set; }

    /// <summary>
    /// Enables CORS for MCP over HTTP.
    /// </summary>
    public bool EnableCors { get; set; } = false;

    /// <summary>
    /// Allowed origins when CORS is enabled.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>
    /// Controls whether the discovery endpoint (/capabilities) is published.
    /// </summary>
    public bool PublishCapabilityEndpoint { get; set; } = true;

    /// <summary>
    /// WEB-0072 — the free-text guidance returned in the MCP <c>initialize</c> response's <c>instructions</c>
    /// field (effectively the system prompt of this MCP surface — what the LLM is told about how to use the
    /// server). When unset, the application description (<c>[KoanApp].Description</c>) is used.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Determines how MCP capabilities are exposed to clients (Auto, Code, Tools, Full).
    /// Auto: Detect client capabilities and adapt (default).
    /// Code: Expose code execution only (token optimized).
    /// Tools: Expose entity tools only (legacy compatibility).
    /// Full: Expose both code and tools (maximum compatibility).
    /// </summary>
    public McpExposureMode? Exposure { get; set; }

    /// <summary>
    /// Code mode execution configuration.
    /// </summary>
    public CodeModeOptions? CodeMode { get; set; }

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

    /// <summary>
    /// P3.2 — per-toolset opt-in for framework-shipped OPERATIONAL MCP toolsets (e.g. <c>Koan.Mcp.Operations</c>'s
    /// <c>jobs</c>/<c>cache</c>). Keyed by the toolset's <c>[McpOperationalToolset]</c> key; ALL default OFF (including
    /// Development) — operational verbs are privileged and grant-gated. A disabled toolset's verbs are absent from
    /// <c>tools/list</c> and uninvocable. Generic by design: core does not know the toolset names.
    /// </summary>
    public Dictionary<string, bool> Operations { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the operational toolset <paramref name="key"/> is explicitly enabled (default OFF).</summary>
    public bool IsOperationalToolsetEnabled(string key)
        => !string.IsNullOrWhiteSpace(key) && Operations.TryGetValue(key, out var on) && on;
}

public sealed class McpEntityOverride
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? AllowMutations { get; set; }
    public string? SchemaOverride { get; set; }
}
