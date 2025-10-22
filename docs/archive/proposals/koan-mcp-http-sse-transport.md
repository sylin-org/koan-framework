# Koan MCP HTTP+SSE Transport Implementation

**Status:** Draft
**Author:** Koan Framework Team
**Date:** September 2025
**Version:** 1.0

## Abstract

This proposal defines the implementation of HTTP + Server-Sent Events (SSE) transport for `Koan.Mcp`, enabling remote network-based MCP clients to interact with Koan entity endpoints. While STDIO transport (Phase 1) provides local agent integration for development scenarios, HTTP+SSE transport unlocks remote access for web-based IDEs, distributed systems, and multi-tenant deployments. The implementation maintains Koan's "Reference = Intent" principle while introducing configurable authentication, session management, and security controls.

## Motivation

### Current Limitations

The existing STDIO transport implementation (Koan v0.6.x) provides:

- ✅ **Local agent integration**: Claude Desktop, Cursor, VS Code agents via subprocess spawning
- ✅ **Development experience**: Zero-config tool exposure for `[McpEntity]` decorated types
- ✅ **Service parity**: Shared execution layer with REST/GraphQL via `IEntityEndpointService`

However, STDIO transport has fundamental constraints:

- ❌ **Local-only access**: Requires client to spawn Koan process as child (not suitable for remote/cloud scenarios)
- ❌ **No authentication**: Relies on OS process trust model
- ❌ **Single session**: One client per process instance
- ❌ **No network distribution**: Cannot serve multiple remote IDE instances

### Target Scenarios

HTTP+SSE transport enables:

1. **Remote IDE Integration**: GitHub Copilot Workspace, Cloud9, Replit connecting to Koan services over HTTP
2. **Multi-Tenant MCP Servers**: Single Koan instance serving multiple authenticated agents
3. **Containerized Deployments**: Koan services running in Kubernetes/Docker exposing MCP via ingress
4. **Cross-Network Access**: Agents calling MCP tools from different networks/VPCs with proper authentication
5. **Browser-Based Agents**: JavaScript/TypeScript MCP clients running in web applications

### Design Principles

- **Koan-style DX**: Package reference → auto-registration → configuration (not code)
- **Secure by Default**: Authentication required in production, optional in development
- **Service Parity**: Identical behavior to REST endpoints (same validation, hooks, diagnostics)
- **Transport Agnostic**: Share executor/registry logic between STDIO and HTTP+SSE
- **Progressive Enhancement**: HTTP+SSE builds on existing Phase 1 infrastructure

### Related Documentation

- **Implementation Guide** – [Expose MCP over HTTP + SSE](../guides/mcp-http-sse-howto.md) walks through enabling the transport, authenticating clients, and validating JSON-RPC traffic end-to-end in under fifteen minutes.
- **Integration Strategy** – [Koan MCP Integration Proposal](koan-mcp-integration.md) tracks the broader MCP roadmap and shows how the HTTP transport fits alongside STDIO and future WebSocket phases.

## Current State Analysis

### What's Implemented (STDIO Transport)

```
src/Koan.Mcp/
  ✅ McpEntityAttribute.cs          - Entity decoration for tool exposure
  ✅ McpEntityRegistry.cs            - Tool discovery and indexing
  ✅ Execution/EndpointToolExecutor.cs - Tool invocation via IEntityEndpointService
  ✅ Hosting/StdioTransport.cs       - STDIO JSON-RPC server (BackgroundService)
  ✅ Hosting/McpServer.cs            - Core orchestration
  ✅ Options/McpServerOptions.cs     - Configuration model
```

**STDIO Transport Flow:**

```
Console.In (stdin)
  → StreamJsonRpc (HeaderDelimitedMessageHandler)
  → McpRpcHandler (tools/list, tools/call)
  → EndpointToolExecutor
  → IEntityEndpointService<T,K>
  → Console.Out (stdout) via StreamJsonRpc
```

### What's Missing (HTTP+SSE Transport)

**Critical Gap:** `HttpSseTransport.cs` is a **placeholder** with no implementation:

```csharp
// Current state - 25 lines of placeholder
internal sealed class HttpSseTransport : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HTTP + SSE MCP transport placeholder...");
        return Task.CompletedTask; // ❌ No functionality
    }
}
```

### Infrastructure Gaps

| Component                | STDIO                   | HTTP+SSE                                                   | Status     |
| ------------------------ | ----------------------- | ---------------------------------------------------------- | ---------- |
| **Transport Layer**      | stdin/stdout streams    | HTTP GET (SSE channel) + HTTP POST (JSON-RPC submit)       | ❌ Missing |
| **Message Framing**      | HeaderDelimited         | SSE events (`data: {...}\n\n`)                             | ❌ Missing |
| **Connection Model**     | Single session          | Multi-client sessions                                      | ❌ Missing |
| **Authentication**       | None (local trust)      | Bearer tokens / OAuth                                      | ❌ Missing |
| **Session Management**   | CancellationTokenSource | HttpSseSessionManager                                      | ❌ Missing |
| **Capability Discovery** | `tools/list` (JSON-RPC) | GET `/mcp/capabilities`                                    | ❌ Missing |
| **Endpoint Mapping**     | N/A                     | GET `/mcp/sse` + POST `/mcp/rpc` + GET `/mcp/capabilities` | ❌ Missing |
| **CORS Support**         | N/A                     | Configurable origins                                       | ❌ Missing |
| **Rate Limiting**        | N/A                     | Per-user limits                                            | ❌ Missing |
| **Health Monitoring**    | ✅ IHealthAggregator    | ❌ Not implemented                                         |

## Review Highlights

### Strengths confirmed during implementation review

- The GET-based `/mcp/sse` handshake plus dedicated POST `/mcp/rpc` endpoint matches mainstream SSE expectations, allowing stock HTTP tooling and reverse proxies to participate without special framing rules.
- Capability discovery is now a first-class HTTP surface at `/mcp/capabilities`, keeping Koan’s “reference = intent” boot story intact while giving dashboards and hosted IDEs a simple JSON contract to inspect.
- Auto-registration through `KoanWebStartupFilter` preserves the familiar configuration-first DX: once `EnableHttpSseTransport` is true the web pipeline maps the endpoints, publishes boot diagnostics, and enforces authorization consistently.

### Remaining gaps and mitigation plan

- `HttpSseRpcBridge` currently routes JSON-RPC methods directly rather than using `IMcpTransportDispatcher`; the manual loop is well-covered by unit tests but is marked for replacement once `StreamJsonRpc` exposes an SSE-friendly message handler.
- Health reporting now surfaces connection counts and keep-alive cadence, but additional transport metrics (latency, payload size) are tracked for a future observability milestone.
- Documentation and samples continue to grow—this proposal links to the new hands-on how-to guide so developers can validate the transport end-to-end while the full sample refresh is in progress.

## Proposed Solution

### Architecture Overview

```
┌─────────────────┐       GET /mcp/sse            ┌────────────────────────┐
│  MCP Client     │ ────────────────────────────> │  SSE Stream Endpoint   │
│  (Browser/IDE)  │ <═══════════════════════════  │  (ASP.NET Core)        │
└─────────────────┘        text/event-stream       └────────────────────────┘
           │                                            │
           │ POST /mcp/rpc (JSON-RPC 2.0)               ▼
           └────────────────────────────────────────> ┌──────────────────────────┐
                                                    │  HttpSseSessionManager    │
                                                    │  - Concurrency limits     │
                                                    │  - Heartbeats & health    │
                                                    └──────────────────────────┘
                                                               │
                                                               ▼
                                                    ┌──────────────────────────┐
                                                    │  StreamJsonRpc Dispatcher│
                                                    │  (IMcpTransportDispatcher│
                                                    │   reused from STDIO)     │
                                                    └──────────────────────────┘
                                                               │
                                                               ▼
                                                    ┌──────────────────────────┐
                                                    │  EndpointToolExecutor    │
                                                    │  (Shared with STDIO)     │
                                                    └──────────────────────────┘

                  Capability Discovery → GET /mcp/capabilities (JSON summary)
```

**Flow summary:**

1. Client opens a long-lived `GET /mcp/sse` stream (optionally including an `access_token` query parameter when headers are unavailable). The server responds with a `connected` SSE event containing the generated `sessionId`.
2. Client submits JSON-RPC requests to `POST /mcp/rpc`, passing the `sessionId` via both `X-Mcp-Session` header and `sessionId` query string. The transport emits `ack` events when a request is accepted and streams results/errors over the same SSE channel using the shared `IMcpTransportDispatcher`.
3. Tooling and health surfaces discover exposed capabilities via `GET /mcp/capabilities`, a simple JSON document mirroring `tools/list` output and transport metadata for compatibility with mainstream SSE deployment patterns.

### Core Components

#### 1. HTTP Endpoint Infrastructure

**File:** `src/Koan.Mcp/Extensions/EndpointExtensions.cs`

This endpoint surface mirrors mainstream SSE deployments: a GET endpoint to establish the stream, a separate POST endpoint for upstream JSON payloads, and a discovery GET route that exposes transport metadata for clients that cannot call `tools/list` until the stream is active.

```csharp
public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapKoanMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var services = endpoints.ServiceProvider;
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<McpServerOptions>>();
        var options = optionsMonitor.CurrentValue;

        if (!options.EnableHttpSseTransport)
        {
            return endpoints;
        }

        var baseRoute = options.HttpSseRoute?.TrimEnd('/') ?? "/mcp";
        var httpTransport = services.GetRequiredService<HttpSseTransport>();
        var capabilityReporter = services.GetRequiredService<IMcpCapabilityReporter>();

        var group = endpoints.MapGroup(baseRoute);
        if (options.RequireAuthentication)
        {
            group.RequireAuthorization();
        }

        group.MapGet("sse", httpTransport.AcceptStreamAsync)
             .Produces("text/event-stream")
             .WithName("KoanMcpSseStream");

        group.MapPost("rpc", httpTransport.SubmitRequestAsync)
             .Accepts<JsonRpcRequest>("application/json")
             .Produces("application/json")
             .WithName("KoanMcpRpcSubmit");

        if (options.PublishCapabilityEndpoint)
        {
            group.MapGet("capabilities", async context =>
            {
                var payload = await capabilityReporter.GetCapabilitiesAsync(context.RequestAborted);
                await context.Response.WriteAsJsonAsync(payload);
            })
            .Produces("application/json")
            .WithName("KoanMcpCapabilities");
        }

        return endpoints;
    }
}
```

**Auto-Registration** (via `KoanWebStartupFilter`, sharing the existing routing block):

```csharp
public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
{
    return app =>
    {
        next(app);

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapKoanEndpoints();       // existing REST/GraphQL wiring
            endpoints.MapKoanMcpEndpoints();    // HTTP+SSE transport routed in the same block
        });
    };
}
```

#### 2. Session Management

**File:** `src/Koan.Mcp/Hosting/HttpSseSessionManager.cs`

```csharp
public sealed class HttpSseSessionManager : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpSseSession> _sessions = new();
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly ISystemClock _clock;
    private readonly IHealthAggregator _health;
    private Timer? _heartbeatTimer;

    public HttpSseSessionManager(
        IOptionsMonitor<McpServerOptions> options,
        ISystemClock clock,
        IHealthAggregator health)
    {
        _options = options;
        _clock = clock;
        _health = health;
    }

    public bool TryOpenSession(HttpContext context, out HttpSseSession session)
    {
        var limit = _options.CurrentValue.MaxConcurrentConnections;
        if (_sessions.Count >= limit)
        {
            session = default!;
            return false;
        }

        var id = Guid.NewGuid().ToString("n");
        session = new HttpSseSession(
            id,
            context.User,
            context.Response.Body,
            CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted),
            _clock.UtcNow);

        if (!_sessions.TryAdd(id, session))
        {
            session.Dispose();
            return false;
        }

        _health.Publish(new McpTransportHealthSnapshot(
            activeConnections: _sessions.Count,
            lastChangeUtc: _clock.UtcNow));

        return true;
    }

    public bool TryGet(string sessionId, out HttpSseSession session)
        => _sessions.TryGetValue(sessionId, out session);

    public void CloseSession(HttpSseSession session)
    {
        if (_sessions.TryRemove(session.Id, out _))
        {
            session.Complete();
            session.Dispose();
            _health.Publish(new McpTransportHealthSnapshot(
                activeConnections: _sessions.Count,
                lastChangeUtc: _clock.UtcNow));
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer = new Timer(_ => BroadcastHeartbeat(), null,
            _options.CurrentValue.Transport.SseKeepAliveInterval,
            _options.CurrentValue.Transport.SseKeepAliveInterval);
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer?.Dispose();
        foreach (var session in _sessions.Values)
        {
            session.Cancellation.Cancel();
            session.Complete();
        }
        return Task.CompletedTask;
    }

    private void BroadcastHeartbeat()
    {
        var payload = new { timestamp = _clock.UtcNow };
        foreach (var session in _sessions.Values)
        {
            session.Enqueue(ServerSentEvent.Heartbeat(payload));
        }
    }

    public void Dispose() => _heartbeatTimer?.Dispose();
}

public readonly record struct McpTransportHealthSnapshot(int ActiveConnections, DateTimeOffset LastChangeUtc)
    : IHealthPayload;

public sealed class HttpSseSession : IDisposable
{
    private readonly Channel<ServerSentEvent> _outbound = Channel.CreateUnbounded<ServerSentEvent>();

    internal HttpSseSession(
        string id,
        ClaimsPrincipal user,
        Stream responseStream,
        CancellationTokenSource cancellation,
        DateTimeOffset createdAt)
    {
        Id = id;
        User = user;
        ResponseStream = responseStream;
        Cancellation = cancellation;
        CreatedAt = createdAt;
        LastActivityUtc = createdAt;
    }

    public string Id { get; }
    public ClaimsPrincipal User { get; }
    public Stream ResponseStream { get; }
    public CancellationTokenSource Cancellation { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastActivityUtc { get; private set; }

    private HttpSseRpcBridge? _bridge;

    public ValueTask<ServerSentEvent> DequeueAsync(CancellationToken ct)
        => _outbound.Reader.ReadAsync(ct);

    public IAsyncEnumerable<ServerSentEvent> OutboundMessages(CancellationToken ct)
        => _outbound.Reader.ReadAllAsync(ct);

    public void Enqueue(ServerSentEvent message)
    {
        LastActivityUtc = DateTimeOffset.UtcNow;
        _outbound.Writer.TryWrite(message);
    }

    public void AttachBridge(HttpSseRpcBridge bridge) => _bridge = bridge;
    public HttpSseRpcBridge Bridge => _bridge ?? throw new InvalidOperationException("Session bridge not initialised");

    public void Complete() => _outbound.Writer.TryComplete();

    public void Dispose() => Cancellation.Dispose();
}
```

#### 3. JSON-RPC Bridge

**File:** `src/Koan.Mcp/Hosting/HttpSseRpcBridge.cs`

```csharp
public sealed class HttpSseRpcBridge : IAsyncDisposable
{
    private readonly Channel<JsonRpcEnvelope> _requests = Channel.CreateUnbounded<JsonRpcEnvelope>(new()
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });
    private readonly CancellationTokenSource _cts;
    private readonly Task _processingTask;
    private readonly McpRpcHandler _handler;

    public HttpSseRpcBridge(
        McpServer server,
        McpEntityRegistry registry,
        IOptionsMonitor<McpServerOptions> options,
        HttpSseSession session,
        ILogger<HttpSseRpcBridge> logger)
    {
        _handler = server.CreateHandler();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token);
        _processingTask = Task.Run(ProcessAsync);
    }

    public ValueTask SubmitAsync(JsonRpcEnvelope request, CancellationToken cancellationToken)
    {
        if (!_requests.Writer.TryWrite(request))
        {
            return new ValueTask(_requests.Writer.WriteAsync(request, cancellationToken).AsTask());
        }

        return ValueTask.CompletedTask;
    }

    private async Task ProcessAsync()
    {
        await foreach (var envelope in _requests.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
        {
            await DispatchAsync(envelope, _cts.Token).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _processingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
```

- `DispatchAsync` forwards `tools/list`, `tools/call`, and `ping` invocations to the shared `McpRpcHandler`, ensuring HTTP + SSE clients continue to reuse Koan’s endpoint metadata, request translation, and diagnostics pipeline.
- Access control honours per-entity `RequireAuthentication` and `RequiredScopes` declarations before invoking the executor, mirroring the STDIO transport’s guardrails.
- Heartbeats, acknowledgements, and final results are streamed back to the client by enqueuing serialized `ServerSentEvent` payloads on the active session.

> **Future improvement:** once StreamJsonRpc exposes a message handler abstraction suited for SSE transports, the bridge can swap to the shared `IMcpTransportDispatcher` to remove the remaining custom dispatch loop.

#### 4. Server-Sent Event Primitives

**File:** `src/Koan.Mcp/Hosting/ServerSentEvent.cs`

```csharp
public readonly record struct ServerSentEvent(string Event, JsonNode Payload)
{
    public static ServerSentEvent Connected(string sessionId, DateTimeOffset timestamp) =>
        new("connected", JsonNode.Parse(JsonSerializer.Serialize(new
        {
            sessionId,
            timestamp
        }))!);

    public static ServerSentEvent Heartbeat(object payload) =>
        new("heartbeat", JsonNode.Parse(JsonSerializer.Serialize(payload))!);

    public static ServerSentEvent FromJsonRpc(JsonRpcMessage message) =>
        new(message is JsonRpcError ? "error" : "result",
            JsonNode.Parse(JsonSerializer.Serialize(message))!);

    public async ValueTask<JsonRpcMessage> ReadAsync(CancellationToken cancellationToken)
    {
        return await _inbound.Reader.ReadAsync(cancellationToken);
    }

    public ValueTask WriteAsync(JsonRpcMessage content, CancellationToken cancellationToken)
    {
        _session.Enqueue(ServerSentEvent.FromJsonRpc(content));
        return ValueTask.CompletedTask;
    }

    public void Dispose() { }
}
```

#### 4. Server-Sent Event Primitives

**File:** `src/Koan.Mcp/Hosting/ServerSentEvent.cs`

```csharp
public readonly record struct ServerSentEvent(string Event, JsonNode Payload)
{
    public static ServerSentEvent Connected(string sessionId, DateTimeOffset timestamp) =>
        new("connected", JsonNode.Parse(JsonSerializer.Serialize(new
        {
            sessionId,
            timestamp
        }))!);

    public static ServerSentEvent Heartbeat(object payload) =>
        new("heartbeat", JsonNode.Parse(JsonSerializer.Serialize(payload))!);

    public static ServerSentEvent FromJsonRpc(JsonRpcMessage message) =>
        new(message is JsonRpcError ? "error" : "result",
            JsonNode.Parse(JsonSerializer.Serialize(message))!);

    public static ServerSentEvent Acknowledged(object? id) =>
        new("ack", JsonNode.Parse(JsonSerializer.Serialize(new { id }))!);

    public string ToWireFormat()
    {
        using var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        Payload.WriteTo(writer);
        writer.Flush();

        var builder = new StringBuilder()
            .Append("event: ").Append(Event).Append('\n')
            .Append("data: ").Append(Encoding.UTF8.GetString(buffer.WrittenSpan)).Append("\n\n");

        return builder.ToString();
    }
}
```

```csharp
internal static class HttpSseHeaders
{
    public const string SessionId = "X-Mcp-Session";
}
```

#### 5. HTTP+SSE Transport Implementation

**File:** `src/Koan.Mcp/Hosting/HttpSseTransport.cs` (replace placeholder)

```csharp
public sealed class HttpSseTransport
{
    private readonly HttpSseSessionManager _sessions;
    private readonly IMcpTransportDispatcher _dispatcher;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly ISystemClock _clock;
    private readonly ILogger<HttpSseTransport> _logger;

    public HttpSseTransport(
        HttpSseSessionManager sessions,
        IMcpTransportDispatcher dispatcher,
        IOptionsMonitor<McpServerOptions> options,
        ISystemClock clock,
        ILogger<HttpSseTransport> logger)
    {
        _sessions = sessions;
        _dispatcher = dispatcher;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task AcceptStreamAsync(HttpContext context)
    {
        var options = _options.CurrentValue;

        if (options.RequireAuthentication && context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
            return;
        }

        if (!_sessions.TryOpenSession(context, out var session))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "max_connections_exceeded" });
            return;
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        await using var bridge = new HttpSseRpcBridge(_dispatcher, session, _logger);
        session.AttachBridge(bridge);
        session.Enqueue(ServerSentEvent.Connected(session.Id, _clock.UtcNow));

        try
        {
            await foreach (var evt in session.OutboundMessages(context.RequestAborted))
            {
                await context.Response.WriteAsync(evt.ToWireFormat(), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("MCP SSE session {SessionId} cancelled", session.Id);
        }
        finally
        {
            _sessions.CloseSession(session);
        }
    }

    public async Task<IResult> SubmitRequestAsync(HttpContext context)
    {
        var sessionId = context.Request.Headers[HttpSseHeaders.SessionId];
        if (StringValues.IsNullOrEmpty(sessionId))
        {
            sessionId = context.Request.Query["sessionId"];
        }

        if (StringValues.IsNullOrEmpty(sessionId) || !_sessions.TryGet(sessionId!, out var session))
        {
            return Results.NotFound(new { error = "unknown_session" });
        }

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        var request = JsonSerializer.Deserialize<JsonRpcRequest>(json);

        if (request is null)
        {
            return Results.BadRequest(new { error = "invalid_jsonrpc" });
        }

        await session.Bridge.SubmitAsync(request, context.RequestAborted);
        session.Enqueue(ServerSentEvent.Acknowledged(request.Id));

        return Results.Accepted($"{context.Request.Path}?sessionId={session.Id}");
    }
}
```

### Configuration Model

**Extend:** `src/Koan.Mcp/Options/McpServerOptions.cs`

```csharp
public sealed class McpServerOptions
{
    // Existing STDIO config
    public bool EnableStdioTransport { get; set; } = true;

    // New HTTP+SSE config
    public bool EnableHttpSseTransport { get; set; } = false; // Opt-in for security
    public string HttpSseRoute { get; set; } = "/mcp";

    // Authentication
    private bool? _requireAuthentication;
    public bool RequireAuthentication
    {
        get => _requireAuthentication ?? (KoanEnv.IsProduction || KoanEnv.InContainer);
        set => _requireAuthentication = value;
    }

    // Connection limits
    public int MaxConcurrentConnections { get; set; } = 100;
    public TimeSpan SseConnectionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    // CORS
    public bool EnableCors { get; set; } = false;
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    // Transport-specific settings
    public McpTransportOptions Transport { get; set; } = new();

    // Capability discovery endpoint toggle
    public bool PublishCapabilityEndpoint { get; set; } = true;

    // Existing options...
    public ISet<string> AllowedEntities { get; } = new HashSet<string>();
    public ISet<string> DeniedEntities { get; } = new HashSet<string>();
    public Dictionary<string, McpEntityOverride> EntityOverrides { get; } = new();
}
```

- `HttpSseRoute` now represents the route prefix (default `/mcp`), producing `/mcp/sse`, `/mcp/rpc`, and `/mcp/capabilities`.
- `PublishCapabilityEndpoint` toggles the discovery endpoint for environments that restrict anonymous metadata exposure.

**Extend:** `src/Koan.Mcp/Options/McpTransportOptions.cs`

```csharp
public sealed class McpTransportOptions
{
    // Existing
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public string LoggerCategory { get; set; } = "Koan.Transport.Mcp";

    // New HTTP+SSE specific
    public int SseBufferSize { get; set; } = 8192;
    public TimeSpan SseKeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
}
```

**Extend:** `src/Koan.Mcp/McpEntityAttribute.cs`

```csharp
[Flags]
public enum McpTransportMode
{
    None = 0,
    Stdio = 1,
    HttpSse = 2,
    All = Stdio | HttpSse
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class McpEntityAttribute : Attribute
{
    // Existing members...

    public bool? RequireAuthentication { get; set; }
    public McpTransportMode EnabledTransports { get; set; } = McpTransportMode.All;
}

public sealed class McpEntityOverride
{
    // Existing members...

    public bool? RequireAuthentication { get; set; }
    public McpTransportMode? EnabledTransports { get; set; }
}
```

### Service Registration

**Update:** `src/Koan.Mcp/Extensions/ServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddKoanMcp(this IServiceCollection services, IConfiguration? configuration = null)
{
    // Existing STDIO registrations...
    services.TryAddSingleton<McpEntityRegistry>();
    services.TryAddSingleton<EndpointToolExecutor>();
    services.TryAddSingleton<IMcpTransportDispatcher, StreamJsonRpcTransportDispatcher>();
    services.TryAddSingleton<McpServer>();
    services.AddHostedService<StdioTransport>();

    // New HTTP+SSE registrations
    services.TryAddSingleton<HttpSseSessionManager>();
    services.AddHostedService(sp => sp.GetRequiredService<HttpSseSessionManager>());
    services.TryAddSingleton<HttpSseTransport>();
    services.TryAddSingleton<IMcpCapabilityReporter, HttpSseCapabilityReporter>();

    // CORS (conditional)
    var options = configuration?.GetSection("Koan:Mcp").Get<McpServerOptions>();
    if (options?.EnableHttpSseTransport == true && options.EnableCors)
    {
        services.AddCors(corsOptions =>
        {
            corsOptions.AddPolicy("KoanMcp", policy =>
            {
                policy.WithOrigins(options.AllowedOrigins)
                      .AllowCredentials()
                      .WithHeaders("Authorization", "Content-Type", HttpSseHeaders.SessionId)
                      .WithMethods("GET", "POST", "OPTIONS");
            });
        });
    }

    return services;
}
```

**File:** `src/Koan.Mcp/Diagnostics/IMcpCapabilityReporter.cs`

```csharp
public interface IMcpCapabilityReporter
{
    Task<McpCapabilityDocument> GetCapabilitiesAsync(CancellationToken cancellationToken);
}

public sealed class HttpSseCapabilityReporter : IMcpCapabilityReporter
{
    private readonly McpEntityRegistry _registry;
    private readonly IOptionsMonitor<McpServerOptions> _options;

    public HttpSseCapabilityReporter(
        McpEntityRegistry registry,
        IOptionsMonitor<McpServerOptions> options)
    {
        _registry = registry;
        _options = options;
    }

    public Task<McpCapabilityDocument> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var tools = _registry.GetAll().Select(entity => new McpCapabilityTool
        {
            Name = entity.Name,
            Description = entity.Description,
            RequireAuthentication = entity.RequireAuthentication ?? options.RequireAuthentication,
            EnabledTransports = entity.EnabledTransports
        }).ToArray();

        return Task.FromResult(new McpCapabilityDocument
        {
            Version = "2.0",
            Transports = new[]
            {
                new McpTransportDescription
                {
                    Kind = "http+sse",
                    StreamEndpoint = options.HttpSseRoute.TrimEnd('/') + "/sse",
                    SubmitEndpoint = options.HttpSseRoute.TrimEnd('/') + "/rpc",
                    CapabilityEndpoint = options.PublishCapabilityEndpoint
                        ? options.HttpSseRoute.TrimEnd('/') + "/capabilities" : null,
                    RequireAuthentication = options.RequireAuthentication
                }
            },
            Tools = tools
        });
    }
}
```

```csharp
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
```

**Update:** `src/Koan.Mcp/Hosting/KoanMcpAutoRegistrar.cs`

```csharp
public Task RegisterAsync(IBootReportWriter writer, CancellationToken cancellationToken)
{
    var options = _options.CurrentValue;

    writer.Section("Model Context Protocol", section =>
    {
        section.Property("STDIO", options.EnableStdioTransport ? "enabled" : "disabled");
        section.Property("HTTP+SSE", options.EnableHttpSseTransport ? "enabled" : "disabled");

        if (options.EnableHttpSseTransport)
        {
            section.Property("Route", options.HttpSseRoute);
            section.Property("Requires Authentication", options.RequireAuthentication);
            section.Property("Capability Endpoint", options.PublishCapabilityEndpoint);
        }
    });

    return Task.CompletedTask;
}
```

## Developer Experience

### Server-Side Setup

#### 1. Entity Declaration (Existing Pattern ✅)

```csharp
using Koan.Mcp;
using Koan.Data.Core.Model;

[McpEntity(
    Name = "TrialSite",
    Description = "Clinical trial site operations",
    RequiredScopes = new[] { "clinical:operations" }
)]
public sealed class TrialSite : Entity<TrialSite>
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    // ... auto-generates CRUD tools
}
```

#### 2. Configuration

**Development (Anonymous Access):**

```jsonc
// appsettings.Development.json
{
  "Koan": {
    "Mcp": {
      "EnableStdioTransport": true, // Local Claude Desktop
      "EnableHttpSseTransport": true, // Remote IDEs
      "RequireAuthentication": false, // ⚠️ Dev only!
      "EnableCors": true,
      "AllowedOrigins": ["http://localhost:3000"]
    }
  }
}
```

**Production (Secure):**

```jsonc
// appsettings.Production.json
{
  "Koan": {
    "Mcp": {
      "EnableStdioTransport": false, // Disable local access
      "EnableHttpSseTransport": true,
      "RequireAuthentication": true, // ✅ Enforced
      "HttpSseRoute": "/mcp",
      "MaxConcurrentConnections": 1000,
      "PublishCapabilityEndpoint": true,
      "EnableCors": true,
      "AllowedOrigins": ["https://ide.example.com"]
    }
  }
}
```

**Hybrid (Mixed Auth):**

```jsonc
{
  "Koan": {
    "Mcp": {
      "EnableHttpSseTransport": true,
      "RequireAuthentication": false, // Global: anonymous OK

      "EntityOverrides": {
        "PublicData": {
          "RequireAuthentication": false
        },
        "SecureData": {
          "RequireAuthentication": true,
          "RequiredScopes": ["admin:read"]
        }
      }
    }
  }
}
```

#### 3. Program.cs (Zero Config)

```csharp
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsProxiedApi();

// MCP auto-registered via KoanAutoRegistrar
builder.Services.AddKoanMcp(builder.Configuration);

var app = builder.Build();

// Endpoints auto-mapped via KoanWebStartupFilter
app.Run();
```

### Client-Side Consumption

#### TypeScript/JavaScript Client

```typescript
// 1. Authenticate (if required)
async function authenticate(): Promise<string> {
  const response = await fetch("http://localhost:5110/.testoauth/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "client_credentials",
      client_id: "mcp-client",
      client_secret: "dev-secret",
      scope: "clinical:operations",
    }),
  });
  return (await response.json()).access_token;
}

type PendingRequest = {
  resolve: (value: any) => void;
  reject: (reason: Error) => void;
};

// 2. MCP Client
class KoanMcpClient {
  private eventSource?: EventSource;
  private sessionId?: string;
  private requestId = 1;
  private readonly pending = new Map<number, PendingRequest>();

  constructor(
    private readonly baseUrl: string,
    private readonly token?: string
  ) {}

  private async ensureConnected(): Promise<void> {
    if (this.eventSource && this.sessionId) {
      return;
    }

    const url = new URL("/mcp/sse", this.baseUrl);
    if (this.token) {
      url.searchParams.set("access_token", this.token);
    }

    this.eventSource = new EventSource(url.toString(), {
      withCredentials: true,
    });

    await new Promise<void>((resolve, reject) => {
      this.eventSource!.addEventListener(
        "connected",
        (event) => {
          const payload = JSON.parse((event as MessageEvent).data);
          this.sessionId = payload.sessionId;
          resolve();
        },
        { once: true }
      );

      this.eventSource!.addEventListener(
        "error",
        () => {
          reject(new Error("Failed to establish MCP SSE session."));
        },
        { once: true }
      );
    });

    this.eventSource.addEventListener("result", (event) => {
      const message = JSON.parse((event as MessageEvent).data);
      const pending = this.pending.get(message.id);
      if (pending) {
        pending.resolve(message.result);
        this.pending.delete(message.id);
      }
    });

    this.eventSource.addEventListener("ack", (event) => {
      const message = JSON.parse((event as MessageEvent).data);
      console.debug("MCP request acknowledged", message.id);
    });

    this.eventSource.addEventListener("error", (event) => {
      const message = JSON.parse((event as MessageEvent).data);
      const pending = this.pending.get(message.id ?? 0);
      if (pending) {
        pending.reject(
          new Error(message.error?.message ?? "Unknown MCP error")
        );
        this.pending.delete(message.id ?? 0);
      }
    });
  }

  async callTool(toolName: string, params: any): Promise<any> {
    await this.ensureConnected();
    if (!this.sessionId) {
      throw new Error("MCP session not established");
    }

    const id = this.requestId++;
    const payload = {
      jsonrpc: "2.0",
      method: "tools/call",
      params: { name: toolName, arguments: params },
      id,
    };

    const headers: Record<string, string> = {
      "Content-Type": "application/json",
      "X-Mcp-Session": this.sessionId,
    };

    if (this.token) {
      headers["Authorization"] = `Bearer ${this.token}`;
    }

    const submitUrl = new URL("/mcp/rpc", this.baseUrl);
    submitUrl.searchParams.set("sessionId", this.sessionId);

    const completion = new Promise<any>((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
    });

    await fetch(submitUrl.toString(), {
      method: "POST",
      headers,
      body: JSON.stringify(payload),
    });

    return completion;
  }
}

// 3. Usage
const token = await authenticate();
const client = new KoanMcpClient("http://localhost:5110", token);

const sites = await client.callTool("TrialSite_List", { pageSize: 10 });
console.log(sites);
```

> ℹ️ When running in a browser, use an `EventSource` polyfill (e.g., `eventsource-polyfill`) if you need to set authorization headers; otherwise rely on HTTPS cookies or the temporary `access_token` query parameter demonstrated above.

#### Python Client

```python
import json
import time
import requests
import sseclient  # pip install sseclient-py


class KoanMcpClient:
    def __init__(self, base_url: str, token: str | None = None) -> None:
        self.base_url = base_url.rstrip('/')
        self.token = token
        self._sse_client: sseclient.SSEClient | None = None
        self._session_id: str | None = None

    def _ensure_connected(self) -> None:
        if self._sse_client and self._session_id:
            return

        headers = {'Accept': 'text/event-stream'}
        params = {}
        if self.token:
            headers['Authorization'] = f'Bearer {self.token}'
            params['access_token'] = self.token

        response = requests.get(
            f"{self.base_url}/mcp/sse",
            headers=headers,
            params=params,
            stream=True,
            timeout=30,
        )
        response.raise_for_status()

        client = sseclient.SSEClient(response)
        for event in client.events():
            if event.event == 'connected':
                payload = json.loads(event.data)
                self._session_id = payload['sessionId']
                break

        self._sse_client = client

    def call_tool(self, tool_name: str, params: dict) -> dict:
        self._ensure_connected()
        assert self._session_id and self._sse_client

        request_id = int(time.time() * 1000)
        payload = {
            'jsonrpc': '2.0',
            'method': 'tools/call',
            'params': {'name': tool_name, 'arguments': params},
            'id': request_id,
        }

        headers = {
            'Content-Type': 'application/json',
            'X-Mcp-Session': self._session_id,
        }
        if self.token:
            headers['Authorization'] = f'Bearer {self.token}'

        response = requests.post(
            f"{self.base_url}/mcp/rpc",
            params={'sessionId': self._session_id},
            headers=headers,
            json=payload,
            timeout=30,
        )
        response.raise_for_status()

        for event in self._sse_client.events():
            body = json.loads(event.data)
            if event.event == 'ack':
                continue
            if event.event == 'result' and body.get('id') == request_id:
                return body['result']
            if event.event == 'error' and body.get('id') == request_id:
                raise RuntimeError(body['error']['message'])

        raise RuntimeError('MCP server closed stream before responding')


# Usage (anonymous dev mode)
client = KoanMcpClient('http://localhost:5110')
sites = client.call_tool('TrialSite_List', {'pageSize': 10})
```

#### cURL Example

```bash
# With authentication
TOKEN=$(curl -X POST http://localhost:5110/.testoauth/token \
  -d "grant_type=client_credentials&client_id=mcp-client&client_secret=dev-secret" \
  | jq -r '.access_token')

# Terminal 1: establish SSE stream (observe `connected` event for sessionId)
curl -N "http://localhost:5110/mcp/sse?access_token=$TOKEN" \
  -H "Accept: text/event-stream" \
  -H "Authorization: Bearer $TOKEN"

# Terminal 2: submit JSON-RPC call using sessionId from the connected event
SESSION_ID="<value from connected event>"
curl -X POST "http://localhost:5110/mcp/rpc?sessionId=$SESSION_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Mcp-Session: $SESSION_ID" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"tools/list","id":1}'

# Optional: discover capabilities over HTTP+SSE
curl -s "http://localhost:5110/mcp/capabilities" \
  -H "Authorization: Bearer $TOKEN" | jq
```

## Authentication & Security

### Authentication Modes

#### Mode 1: Environment-Aware Default (Recommended)

```csharp
// Options default based on environment
public bool RequireAuthentication
{
    get => _requireAuthentication ?? (KoanEnv.IsProduction || KoanEnv.InContainer);
    set => _requireAuthentication = value;
}
```

**Behavior:**

- **Production/Container**: Authentication **required** (default)
- **Development**: Authentication **optional** (default)
- **Explicit config**: Always respected (override environment detection)

#### Mode 2: Explicit Configuration

```jsonc
{
  "Koan": {
    "Mcp": {
      "RequireAuthentication": true // Override environment detection
    }
  }
}
```

#### Mode 3: Per-Entity Control

```csharp
// Public entity - no auth
[McpEntity(Name = "PublicData", RequireAuthentication = false, EnabledTransports = McpTransportMode.HttpSse)]
public sealed class PublicData : Entity<PublicData> { }

// Secure entity - auth required (overrides global)
[McpEntity(
    Name = "SecureData",
    RequireAuthentication = true,
    EnabledTransports = McpTransportMode.All,
    RequiredScopes = new[] { "admin:write" }
)]
public sealed class SecureData : Entity<SecureData> { }
```

### Security Controls

**CORS Configuration:**

```csharp
if (options.EnableCors && options.AllowedOrigins.Any())
{
    services.AddCors(cors => cors.AddPolicy("KoanMcp", policy =>
    {
        policy.WithOrigins(options.AllowedOrigins)
              .AllowCredentials()
              .WithHeaders("Authorization", "Content-Type", HttpSseHeaders.SessionId)
              .WithMethods("GET", "POST", "OPTIONS");
    }));
}
```

**Rate Limiting Integration:**

```csharp
// Leverage existing Koan.Web rate limiting
if (options.EnableRateLimiting)
{
    app.UseRateLimiter(); // Applied before MCP endpoints
}
```

**TLS Enforcement (Production):**

```csharp
if ((KoanEnv.IsProduction || KoanEnv.InContainer) && !httpContext.Request.IsHttps)
{
    httpContext.Response.StatusCode = 400;
    await httpContext.Response.WriteAsJsonAsync(new
    {
        error = "https_required",
        message = "MCP HTTP+SSE requires HTTPS in production"
    });
    return;
}
```

**SSE Authentication Guidance:**

- Prefer `Authorization` headers or authenticated cookies whenever possible. When browser-native `EventSource` cannot send headers, allow short-lived `access_token` query parameters only over HTTPS and with rate limiting enabled.
- The transport rejects query-string tokens when HTTPS is not negotiated in production/container environments.
- Clients should send both the `X-Mcp-Session` header and the `sessionId` query parameter when posting JSON-RPC payloads to accommodate caches and tracing.

### Security Warning System

```csharp
// Startup warning for insecure configurations
if (!options.RequireAuthentication && (KoanEnv.IsProduction || KoanEnv.InContainer))
{
    _logger.LogWarning(
        "SECURITY WARNING: MCP HTTP+SSE transport running without authentication in {Environment}. " +
        "Set Koan:Mcp:RequireAuthentication=true or enable per-entity auth controls.",
        KoanEnv.Environment
    );
}
```

## Implementation Plan

### Phase 1: Core Infrastructure (Week 1-2)

**Priority: Critical**

- [ ] Implement `HttpSseSessionManager` with concurrent session enforcement and heartbeat scheduling
- [ ] Implement `HttpSseSession` channel pipeline (enqueue/dequeue, bridge attachment)
- [ ] Create `ServerSentEvent` + `HttpSseHeaders` primitives
- [ ] Implement `HttpSseRpcBridge` leveraging existing `IMcpTransportDispatcher`
- [ ] Replace `HttpSseTransport` placeholder with GET `/sse` accept + POST `/rpc` submit flows

**Deliverables:**

- Working `/mcp/sse` stream endpoint
- `/mcp/rpc` submission endpoint using StreamJsonRpc
- Multi-client session support with connection limits

### Phase 2: Configuration & Registration (Week 2)

**Priority: Critical**

- [ ] Extend `McpServerOptions` with HTTP+SSE flags (`HttpSseRoute`, `PublishCapabilityEndpoint`)
- [ ] Extend `McpTransportOptions` with SSE-specific settings
- [ ] Update `McpEntityAttribute`/`McpEntityOverride` with `RequireAuthentication` + transport modes
- [ ] Update `ServiceCollectionExtensions.AddKoanMcp()` for session manager hosted service + capability reporter
- [ ] Create `EndpointExtensions.MapKoanMcpEndpoints()` mapping GET `/sse`, POST `/rpc`, GET `/capabilities`
- [ ] Update `KoanWebStartupFilter` to reuse the existing endpoint routing block
- [ ] Add environment-aware authentication defaults (production requires auth)
- [ ] Update `KoanMcpAutoRegistrar` to include HTTP+SSE state in boot report

**Deliverables:**

- Configuration model complete (options + overrides)
- Auto-registration working with boot report coverage
- Endpoint mapping functional across transports

### Phase 3: Authentication & Security (Week 3)

**Priority: High**

- [ ] Integrate bearer token validation from `httpContext.User`
- [ ] Implement scope enforcement (check `RequiredScopes`)
- [ ] Wire per-entity authentication overrides + transport filters
- [ ] Implement CORS configuration allowing GET/POST + `X-Mcp-Session`
- [ ] Enforce HTTPS for authenticated production requests
- [ ] Add SSE authentication guidance + query-token guardrails
- [ ] Add HTTP error responses (401, 403, 429, 500)
- [ ] Add security warning system for insecure configs

**Deliverables:**

- Authentication working (bearer tokens, scopes)
- CORS configured for SSE + RPC
- Security warnings operational

### Phase 4: Monitoring & Diagnostics (Week 3)

**Priority: Medium**

- [ ] Implement health reporting (track active connections) via `IHealthAggregator`
- [ ] Add SSE heartbeat loop (periodic keep-alive events)
- [ ] Structured logging with session IDs + request IDs
- [ ] Error/result/ack event streaming for MCP clients
- [ ] Integration with existing Koan observability + boot report

**Deliverables:**

- Health metrics exposed
- Heartbeat operational
- Logging integrated

### Phase 5: Testing & Documentation (Week 4)

**Priority: Medium**

- [ ] Unit tests for `HttpSseSessionManager` lifecycle (limits, heartbeats)
- [ ] Integration tests for auth flow
- [ ] Load testing (concurrent connections + long-running sessions)
- [ ] Security testing (invalid tokens, CORS violations)
- [ ] Update S12.MedTrials sample with HTTP+SSE config and capability endpoint
- [ ] Write client SDK examples (TypeScript, Python) with GET/POST flow
- [ ] Document security best practices and SSE auth guidance

**Deliverables:**

- Test coverage >80%
- S12 sample updated
- Client SDKs documented

### Effort Estimation

| Phase                        | Duration       | Priority |
| ---------------------------- | -------------- | -------- |
| Core Infrastructure          | 5-7 days       | Critical |
| Configuration & Registration | 2-3 days       | Critical |
| Authentication & Security    | 3-4 days       | High     |
| Monitoring & Diagnostics     | 2-3 days       | Medium   |
| Testing & Documentation      | 3-4 days       | Medium   |
| **Total**                    | **15-21 days** | -        |

**Team Size:** 1-2 senior developers
**Target Release:** Koan v0.7.0

## Success Criteria

### Functional Requirements

- ✅ HTTP+SSE transport exposes all `[McpEntity]` decorated types
- ✅ Identical tool results to REST endpoints (service parity)
- ✅ Support 100+ concurrent SSE connections
- ✅ Authentication via bearer tokens (OAuth 2.0 compatible)
- ✅ Per-entity scope enforcement
- ✅ CORS configurable for browser clients
- ✅ Graceful shutdown with active connections

### Non-Functional Requirements

- ✅ Zero-config enablement in development
- ✅ Secure-by-default in production
- ✅ <100ms latency from request to first SSE event
- ✅ <5MB memory overhead per SSE connection
- ✅ Health metrics exposed via existing Koan observability
- ✅ Structured logging with correlation IDs

### Developer Experience

- ✅ Package reference → auto-registration (no manual setup)
- ✅ Configuration-driven (not code-driven)
- ✅ Compatible with existing `Koan.Web.Auth` providers
- ✅ Sample client SDKs (TypeScript, Python)
- ✅ Deployment guides (Docker, Kubernetes, reverse proxy)

## Open Questions

1. **WebSocket Transport Priority**: Should WebSocket transport (Phase 3) be prioritized over HTTP+SSE, or wait for MCP protocol stability?

   - **Recommendation**: Defer WebSocket to Phase 3; HTTP+SSE covers 90% of use cases

2. **Streaming Tool Results**: Should individual tool calls support streaming responses (multiple SSE events per tool)?

   - **Recommendation**: Add in Phase 3 as opt-in via `[McpEntity(EnableStreaming = true)]`

3. **Request Batching**: Should HTTP+SSE support batched JSON-RPC requests (multiple tools in one POST)?

   - **Recommendation**: Yes, add in Phase 2 as part of dispatcher implementation

4. **Session Persistence**: Should sessions persist across HTTP requests (session ID in query string)?
   - **Recommendation**: No for initial implementation; each POST is stateless SSE connection

## References

- [Model Context Protocol Specification](https://spec.modelcontextprotocol.io/)
- [Server-Sent Events (SSE) Standard](https://html.spec.whatwg.org/multipage/server-sent-events.html)
- Koan Decision: AI-0012 - MCP JSON-RPC Runtime Standard
- Koan Proposal: koan-mcp-integration.md (Phase 1 STDIO implementation)
- Sample Implementation: S12.MedTrials (STDIO transport reference)

## Appendix: SSE Event Format

### Connection Event

```
event: connected
data: {"sessionId":"abc123","timestamp":"2025-09-24T10:00:00Z"}

```

### Result Event

```
event: result
data: {"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"TrialSite_List","description":"List trial sites"}]}}

```

### Error Event

```
event: error
data: {"jsonrpc":"2.0","id":1,"error":{"code":-32600,"message":"Invalid request"}}

```

### Acknowledgement Event

```
event: ack
data: {"id":1}

```

### Heartbeat Event

```
event: heartbeat
data: {"timestamp":"2025-09-24T10:00:15Z"}

```

### End Event

```
event: end
data: {"timestamp":"2025-09-24T10:00:30Z"}

```

---

**Status Update:** Ready for review and approval
**Next Steps:** Technical review → Implementation kickoff → S12 sample integration
