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
    /// AI-0037 — opt-in for the DEPRECATED legacy HTTP+SSE transport (the 2-endpoint <c>{baseRoute}/sse</c> +
    /// <c>{baseRoute}/rpc</c> 2024-11-05 shape), for clients that have not migrated to Streamable HTTP. Default
    /// false — even when the HTTP master switch is on, Streamable is the default and legacy is explicit opt-in.
    /// </summary>
    public bool EnableLegacySseTransport { get; set; } = false;

    /// <summary>
    /// Base route for Streamable HTTP (for example, <c>/mcp</c>). The deprecated legacy transport,
    /// when explicitly enabled, derives its <c>/sse</c> and <c>/rpc</c> routes from this value.
    /// </summary>
    public string HttpRoute { get; set; } = "/mcp";

    /// <summary>
    /// SEC-0006 D2 — the canonical OAuth resource identifier (RFC 8707 <c>aud</c>) for this MCP edge, e.g.
    /// <c>https://app.example.com/mcp</c>. When set, it is the fixed audience the edge enforces and advertises,
    /// independent of the request <c>Host</c> header. When unset, the resource id is the application's canonical
    /// base URL (<c>Koan:Web:ApplicationUrl</c>, WEB-0078, or the URL the application accepted, WEB-0081) plus
    /// <see cref="HttpRoute"/> — the same host-spoof-proof posture without a second setting; with neither (Development
    /// default) it derives from the live request host.
    /// </summary>
    public string? ResourceUri { get; set; }

    /// <summary>
    /// SEC-0037 — this edge is part of the application, so a bearer issued for the application's own resource is good
    /// here too: <c>Koan:Web:Auth:ResourceServer:Resource</c> when set, else the application's base URL, the one its
    /// ordinary routes accept. The edge still advertises its own resource, and a token for it still works here and
    /// nowhere else, so a client that knows only this edge holds the narrower token. A token for any other resource is still
    /// refused. A token for the application then reaches every tool its subject may call, a verb with no route included.
    /// Outside Development only a configured or accepted URL names the application or this edge, never a request's <c>Host</c>. On, the
    /// edge requires authentication unless <see cref="RequireAuthentication"/> says otherwise. False, the default, keeps
    /// the edge's own audience alone.
    /// </summary>
    public bool AcceptApplicationBearer { get; set; }

    private bool? _requireAuthentication;

    /// <summary>
    /// Indicates whether MCP HTTP endpoints require authentication. Defaults to true in production or container
    /// environments, and when the edge accepts the application's bearer (<see cref="AcceptApplicationBearer"/>), whose
    /// clients start OAuth only on a 401.
    /// </summary>
    public bool RequireAuthentication
    {
        get => _requireAuthentication ?? (AcceptApplicationBearer || KoanEnv.Gate.LooksDeployed());
        set => _requireAuthentication = value;
    }

    /// <summary>
    /// Maximum concurrent HTTP sessions allowed.
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 100;

    /// <summary>
    /// Maximum idle duration before a session is reclaimed.
    /// </summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

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
