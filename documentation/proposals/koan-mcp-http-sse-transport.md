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

| Component | STDIO | HTTP+SSE | Status |
|-----------|-------|----------|--------|
| **Transport Layer** | stdin/stdout streams | HTTP POST + SSE stream | ❌ Missing |
| **Message Framing** | HeaderDelimited | SSE events (`data: {...}\n\n`) | ❌ Missing |
| **Connection Model** | Single session | Multi-client sessions | ❌ Missing |
| **Authentication** | None (local trust) | Bearer tokens / OAuth | ❌ Missing |
| **Session Management** | CancellationTokenSource | SseSessionManager | ❌ Missing |
| **Endpoint Mapping** | N/A | POST /mcp/sse route | ❌ Missing |
| **CORS Support** | N/A | Configurable origins | ❌ Missing |
| **Rate Limiting** | N/A | Per-user limits | ❌ Missing |
| **Health Monitoring** | ✅ IHealthAggregator | ❌ Not implemented |

## Proposed Solution

### Architecture Overview

```
┌─────────────────┐     HTTP POST          ┌──────────────────────────┐
│  MCP Client     │ ─────────────────────> │  /mcp/sse Endpoint       │
│  (Browser/IDE)  │ <────────────────────  │  (ASP.NET Core)          │
└─────────────────┘     SSE Events         └──────────────────────────┘
                                                       │
                                                       ▼
                                            ┌──────────────────────────┐
                                            │  SseSessionManager       │
                                            │  - Multi-client tracking │
                                            │  - Session lifecycle     │
                                            └──────────────────────────┘
                                                       │
                                                       ▼
                                            ┌──────────────────────────┐
                                            │  SseTransportDispatcher  │
                                            │  - JSON-RPC parsing      │
                                            │  - SSE event formatting  │
                                            └──────────────────────────┘
                                                       │
                                                       ▼
                                            ┌──────────────────────────┐
                                            │  EndpointToolExecutor    │
                                            │  (Shared with STDIO)     │
                                            └──────────────────────────┘
```

### Core Components

#### 1. HTTP Endpoint Infrastructure

**File:** `src/Koan.Mcp/Extensions/EndpointExtensions.cs`

```csharp
public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapKoanMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var services = endpoints.ServiceProvider;
        var options = services.GetRequiredService<IOptionsMonitor<McpServerOptions>>().CurrentValue;

        if (!options.EnableHttpSseTransport) return endpoints;

        var route = options.HttpSseRoute ?? "/mcp/sse";

        endpoints.MapPost(route, async (HttpContext context) =>
        {
            var transport = services.GetRequiredService<HttpSseTransport>();
            await transport.HandleConnectionAsync(context);
        })
        .RequireAuthorization() // Conditional based on options.RequireAuthentication
        .WithName("KoanMcpSse")
        .WithMetadata(new ProducesAttribute("text/event-stream"));

        return endpoints;
    }
}
```

**Auto-Registration** (via `KoanWebStartupFilter`):
```csharp
public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
{
    return app =>
    {
        next(app);

        var mcpOptions = app.ApplicationServices.GetService<IOptions<McpServerOptions>>()?.Value;
        if (mcpOptions?.EnableHttpSseTransport == true)
        {
            app.UseEndpoints(endpoints => endpoints.MapKoanMcpEndpoints());
        }
    };
}
```

#### 2. Session Management

**File:** `src/Koan.Mcp/Hosting/SseSessionManager.cs`

```csharp
public sealed class SseSessionManager
{
    private readonly ConcurrentDictionary<string, SseSession> _sessions = new();

    public SseSession CreateSession(HttpContext httpContext)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new SseSession
        {
            SessionId = sessionId,
            User = httpContext.User,
            ResponseStream = httpContext.Response.Body,
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted),
            CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow
        };

        _sessions.TryAdd(sessionId, session);
        return session;
    }

    public void CloseSession(string sessionId) { /* ... */ }
    public SseSession? GetSession(string sessionId) { /* ... */ }
    public IEnumerable<SseSession> GetActiveSessions() { /* ... */ }
    public int ActiveSessionCount => _sessions.Count;
}

public sealed class SseSession
{
    public required string SessionId { get; init; }
    public required ClaimsPrincipal User { get; init; }
    public required Stream ResponseStream { get; init; }
    public required CancellationTokenSource Cancellation { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; set; }
}
```

#### 3. SSE Transport Dispatcher

**File:** `src/Koan.Mcp/Hosting/SseTransportDispatcher.cs`

```csharp
public interface ISseTransportDispatcher
{
    Task HandleRequestAsync(
        HttpContext httpContext,
        McpRpcHandler handler,
        CancellationToken cancellationToken);
}

public sealed class SseTransportDispatcher : ISseTransportDispatcher
{
    public async Task HandleRequestAsync(HttpContext httpContext, McpRpcHandler handler, CancellationToken ct)
    {
        // 1. Parse JSON-RPC request from POST body
        var requestJson = await new StreamReader(httpContext.Request.Body).ReadToEndAsync(ct);
        var requestNode = JsonNode.Parse(requestJson) as JsonObject;

        var method = requestNode?["method"]?.GetValue<string>();
        var paramsNode = requestNode?["params"] as JsonObject;
        var id = requestNode?["id"];

        // 2. Route to handler method
        object result = method switch
        {
            "tools/list" => await handler.ListToolsAsync(ct),
            "tools/call" => await handler.CallToolAsync(
                JsonSerializer.Deserialize<McpRpcHandler.ToolsCallParams>(paramsNode?.ToJsonString() ?? "{}"),
                ct),
            _ => throw new InvalidOperationException($"Unknown method: {method}")
        };

        // 3. Format as SSE event
        await WriteSseEventAsync(httpContext.Response.Body, "result", new
        {
            jsonrpc = "2.0",
            id = id,
            result = result
        });
    }

    private static async Task WriteSseEventAsync(Stream stream, string eventName, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var sseData = $"event: {eventName}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseData);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}
```

#### 4. HTTP+SSE Transport Implementation

**File:** `src/Koan.Mcp/Hosting/HttpSseTransport.cs` (replace placeholder)

```csharp
public sealed class HttpSseTransport
{
    private readonly McpServer _server;
    private readonly SseSessionManager _sessionManager;
    private readonly ISseTransportDispatcher _dispatcher;
    private readonly IOptionsMonitor<McpServerOptions> _optionsMonitor;
    private readonly ILogger<HttpSseTransport> _logger;

    public async Task HandleConnectionAsync(HttpContext httpContext)
    {
        var options = _optionsMonitor.CurrentValue;

        // 1. Authentication check
        if (options.RequireAuthentication && httpContext.User?.Identity?.IsAuthenticated != true)
        {
            httpContext.Response.StatusCode = 401;
            await httpContext.Response.WriteAsJsonAsync(new { error = "unauthorized" });
            return;
        }

        // 2. Setup SSE headers
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no"; // Nginx compatibility

        // 3. Create session
        var session = _sessionManager.CreateSession(httpContext);
        _logger.LogInformation("MCP SSE session {SessionId} created for user {User}",
            session.SessionId, httpContext.User?.Identity?.Name ?? "anonymous");

        try
        {
            // 4. Send connection event
            await WriteSseEventAsync(httpContext.Response.Body, "connected", new
            {
                sessionId = session.SessionId,
                timestamp = DateTimeOffset.UtcNow
            });

            // 5. Process request via dispatcher
            var handler = _server.CreateHandler();
            await _dispatcher.HandleRequestAsync(httpContext, handler, httpContext.RequestAborted);

            // 6. Send completion event
            await WriteSseEventAsync(httpContext.Response.Body, "end", new
            {
                timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in MCP SSE session {SessionId}", session.SessionId);
            await WriteSseEventAsync(httpContext.Response.Body, "error", new
            {
                code = "internal_error",
                message = "An unexpected error occurred"
            });
        }
        finally
        {
            _sessionManager.CloseSession(session.SessionId);
        }
    }

    private static async Task WriteSseEventAsync(Stream stream, string eventName, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var sseData = $"event: {eventName}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseData);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
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
    public string HttpSseRoute { get; set; } = "/mcp/sse";

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

    // Existing options...
    public ISet<string> AllowedEntities { get; } = new HashSet<string>();
    public ISet<string> DeniedEntities { get; } = new HashSet<string>();
    public Dictionary<string, McpEntityOverride> EntityOverrides { get; } = new();
}
```

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
    services.TryAddSingleton<SseSessionManager>();
    services.TryAddSingleton<ISseTransportDispatcher, SseTransportDispatcher>();
    services.TryAddSingleton<HttpSseTransport>();

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
                      .WithHeaders("Authorization", "Content-Type")
                      .WithMethods("POST", "OPTIONS");
            });
        });
    }

    return services;
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
      "EnableStdioTransport": true,      // Local Claude Desktop
      "EnableHttpSseTransport": true,    // Remote IDEs
      "RequireAuthentication": false,    // ⚠️ Dev only!
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
      "EnableStdioTransport": false,     // Disable local access
      "EnableHttpSseTransport": true,
      "RequireAuthentication": true,     // ✅ Enforced
      "HttpSseRoute": "/mcp/sse",
      "MaxConcurrentConnections": 1000,
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
      "RequireAuthentication": false,    // Global: anonymous OK

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
    .AsWebApi()
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
  const response = await fetch('http://localhost:5110/.testoauth/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'client_credentials',
      client_id: 'mcp-client',
      client_secret: 'dev-secret',
      scope: 'clinical:operations'
    })
  });
  return (await response.json()).access_token;
}

// 2. MCP Client
class KoanMcpClient {
  constructor(private baseUrl: string, private token?: string) {}

  async callTool(toolName: string, params: any): Promise<any> {
    const headers: any = {
      'Content-Type': 'application/json',
      'Accept': 'text/event-stream'
    };

    if (this.token) {
      headers['Authorization'] = `Bearer ${this.token}`;
    }

    const response = await fetch(`${this.baseUrl}/mcp/sse`, {
      method: 'POST',
      headers,
      body: JSON.stringify({
        jsonrpc: '2.0',
        method: 'tools/call',
        params: { name: toolName, arguments: params },
        id: Date.now()
      })
    });

    return this.parseSSEResponse(response);
  }

  private async parseSSEResponse(response: Response): Promise<any> {
    const reader = response.body!.getReader();
    const decoder = new TextDecoder();

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      const text = decoder.decode(value);
      const event = this.extractSSEEvent(text);

      if (event?.type === 'result') {
        return event.data.result;
      } else if (event?.type === 'error') {
        throw new Error(event.data.message);
      }
    }
  }
}

// 3. Usage
const token = await authenticate();
const client = new KoanMcpClient('http://localhost:5110', token);

const sites = await client.callTool('TrialSite_List', { pageSize: 10 });
console.log(sites);
```

#### Python Client

```python
import requests
import sseclient  # pip install sseclient-py

class KoanMcpClient:
    def __init__(self, base_url, token=None):
        self.base_url = base_url
        self.token = token

    def call_tool(self, tool_name, params):
        headers = {
            'Content-Type': 'application/json',
            'Accept': 'text/event-stream'
        }
        if self.token:
            headers['Authorization'] = f'Bearer {self.token}'

        response = requests.post(
            f"{self.base_url}/mcp/sse",
            json={
                'jsonrpc': '2.0',
                'method': 'tools/call',
                'params': {'name': tool_name, 'arguments': params},
                'id': 1
            },
            headers=headers,
            stream=True
        )

        client = sseclient.SSEClient(response)
        for event in client.events():
            if event.event == 'result':
                return json.loads(event.data)['result']
            elif event.event == 'error':
                raise Exception(json.loads(event.data)['message'])

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

curl -X POST http://localhost:5110/mcp/sse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: text/event-stream" \
  -d '{"jsonrpc":"2.0","method":"tools/list","id":1}'

# Response (SSE format):
# event: result
# data: {"jsonrpc":"2.0","id":1,"result":{"tools":[...]}}
#
# event: end
# data: {"timestamp":"2025-09-24T12:34:56Z"}
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
      "RequireAuthentication": true  // Override environment detection
    }
  }
}
```

#### Mode 3: Per-Entity Control

```csharp
// Public entity - no auth
[McpEntity(Name = "PublicData", RequireAuthentication = false)]
public sealed class PublicData : Entity<PublicData> { }

// Secure entity - auth required (overrides global)
[McpEntity(
    Name = "SecureData",
    RequireAuthentication = true,
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
              .WithHeaders("Authorization", "Content-Type")
              .WithMethods("POST", "OPTIONS");
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

- [ ] Create `SseSessionManager` with concurrent session tracking
- [ ] Create `SseSession` model (sessionId, user, stream, cancellation)
- [ ] Implement `ISseTransportDispatcher` interface
- [ ] Implement `SseTransportDispatcher` (JSON-RPC → SSE events)
- [ ] Replace `HttpSseTransport` placeholder with full implementation
- [ ] Add SSE event writer (`WriteSseEventAsync`)
- [ ] Implement SSE header setup and connection lifecycle

**Deliverables:**
- Working `/mcp/sse` endpoint
- SSE event streaming
- Multi-client session support

### Phase 2: Configuration & Registration (Week 2)

**Priority: Critical**

- [ ] Extend `McpServerOptions` with HTTP+SSE flags
- [ ] Extend `McpTransportOptions` with SSE-specific settings
- [ ] Update `ServiceCollectionExtensions.AddKoanMcp()` for HTTP+SSE
- [ ] Create `EndpointExtensions.MapKoanMcpEndpoints()` method
- [ ] Update `KoanWebStartupFilter` to auto-map endpoints
- [ ] Add environment-aware authentication defaults

**Deliverables:**
- Configuration model complete
- Auto-registration working
- Endpoint mapping functional

### Phase 3: Authentication & Security (Week 3)

**Priority: High**

- [ ] Integrate bearer token validation from `httpContext.User`
- [ ] Implement scope enforcement (check `RequiredScopes`)
- [ ] Add per-entity authentication overrides
- [ ] Implement CORS configuration and middleware
- [ ] Add HTTP error responses (401, 403, 429, 500)
- [ ] Add security warning system for insecure configs

**Deliverables:**
- Authentication working (bearer tokens, scopes)
- CORS configured
- Security warnings operational

### Phase 4: Monitoring & Diagnostics (Week 3)

**Priority: Medium**

- [ ] Implement health reporting (track active connections)
- [ ] Add SSE heartbeat loop (periodic keep-alive events)
- [ ] Structured logging with session IDs
- [ ] Error event streaming for MCP clients
- [ ] Integration with existing Koan observability

**Deliverables:**
- Health metrics exposed
- Heartbeat operational
- Logging integrated

### Phase 5: Testing & Documentation (Week 4)

**Priority: Medium**

- [ ] Unit tests for `SseSessionManager` lifecycle
- [ ] Integration tests for auth flow
- [ ] Load testing (concurrent connections)
- [ ] Security testing (invalid tokens, CORS violations)
- [ ] Update S12.MedTrials sample with HTTP+SSE config
- [ ] Write client SDK examples (TypeScript, Python)
- [ ] Document security best practices

**Deliverables:**
- Test coverage >80%
- S12 sample updated
- Client SDKs documented

### Effort Estimation

| Phase | Duration | Priority |
|-------|----------|----------|
| Core Infrastructure | 5-7 days | Critical |
| Configuration & Registration | 2-3 days | Critical |
| Authentication & Security | 3-4 days | High |
| Monitoring & Diagnostics | 2-3 days | Medium |
| Testing & Documentation | 3-4 days | Medium |
| **Total** | **15-21 days** | - |

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