---
type: GUIDE
domain: web
title: "Expose MCP over HTTP + SSE"
audience: [developers, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Expose MCP over HTTP + SSE

**Document Type**: GUIDE \
**Target Audience**: Developers, AI Agents \
**Last Updated**: 2025-01-17 \
**Framework Version**: v0.2.18+

---

Get a Koan MCP server streaming over HTTP + Server-Sent Events (SSE) in minutes. This how-to covers:

1. Enabling the HTTP transport
2. Decorating entities for MCP exposure
3. Running the server
4. Calling `tools/list` and `tools/call` via SSE
5. Surfacing discovery metadata with `/mcp/capabilities`

## 1. Prerequisites

- **.NET 9 SDK** or later
- **Koan packages**: `Koan.Core`, `Koan.Web`, `Koan.Mcp`
- (Optional) **curl** and **jq** for quick validation

## 2. Create the Project

```bash
mkdir koan-mcp-http && cd koan-mcp-http
dotnet new web

dotnet add package Koan.Core
dotnet add package Koan.Web
dotnet add package Koan.Mcp
```

## 3. Add an MCP Entity

```csharp
// Models/Todo.cs
using Koan.Data;
using Koan.Mcp;

[McpEntity(Description = "Simple task management", AllowMutations = true)]
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
}
```

Koan will automatically surface CRUD operations for any `[McpEntity]` via the shared endpoint executor.

## 4. Configure HTTP + SSE Transport

Add the MCP section to your `appsettings.Development.json`:

```jsonc
{
  "Koan": {
    "Mcp": {
      "EnableHttpSseTransport": true,
      "RequireAuthentication": false,
      "EnableCors": true,
      "AllowedOrigins": ["http://localhost:3000"],
      "PublishCapabilityEndpoint": true
    }
  }
}
```

> ⚠️ Authentication stays disabled only for local development. Production must require HTTPS and auth.

## 5. Wire Up Program.cs

```csharp
using Koan.Mcp.Extensions;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();

builder.Services.AddKoanMcp(builder.Configuration);

var app = builder.Build();

app.Run();
```

`AddKoanMcp` binds `Koan:Mcp` options, registers the STDIO and HTTP transports, and lets the `KoanWebStartupFilter` map `/mcp/*` endpoints automatically.

## 6. Run the Server

```bash
dotnet run
```

You should see the boot report highlight the HTTP + SSE transport with its route and authentication posture.

## 7. Open the SSE Stream

In a new terminal, stream events and capture the session identifier:

```bash
curl -N http://localhost:5110/mcp/sse
```

Sample output:

```
event: connected
data: {"sessionId":"0f3c2c0d7f7444f9b3a0f9278f6b8a8f","timestamp":"2025-01-17T21:42:01.123Z"}
```

Leave this terminal open; the server will push `ack`, `result`, `error`, and `heartbeat` events over the same connection.

## 8. List Tools via JSON-RPC

Use the `sessionId` from the previous step when posting JSON-RPC requests:

```bash
SESSION="0f3c2c0d7f7444f9b3a0f9278f6b8a8f"

curl -X POST http://localhost:5110/mcp/rpc \
  -H "Content-Type: application/json" \
  -H "X-Mcp-Session: $SESSION" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

The SSE window will emit an acknowledgement followed by a tool list result. Use `jq` locally to inspect the JSON payload if needed.

## 9. Invoke a Tool

```bash
curl -X POST http://localhost:5110/mcp/rpc \
  -H "Content-Type: application/json" \
  -H "X-Mcp-Session: $SESSION" \
  -d '{
        "jsonrpc":"2.0",
        "id":2,
        "method":"tools/call",
        "params":{
          "name":"Todo.create",
          "arguments":{
            "title":"Ship HTTP SSE",
            "isCompleted":false
          }
        }
      }'
```

The response stream will contain the created entity and any validation diagnostics emitted by Koan’s shared executor.

## 10. Discover Capabilities

The `/mcp/capabilities` endpoint exposes transport metadata for dashboards and managed clients:

```bash
curl http://localhost:5110/mcp/capabilities | jq
```

The payload includes transport routes, authentication requirements, and tool descriptors (mirroring `tools/list`).

## 11. Secure the Transport

For staging and production environments:

```jsonc
{
  "Koan": {
    "Mcp": {
      "EnableHttpSseTransport": true,
      "RequireAuthentication": true,
      "MaxConcurrentConnections": 500,
      "AllowedOrigins": ["https://ide.example.com"],
      "EntityOverrides": {
        "Todo": {
          "RequireAuthentication": true,
          "RequiredScopes": ["todos:write"]
        }
      }
    }
  }
}
```

- Enforce HTTPS and reverse proxy headers in production.
- Issue OAuth tokens (or other supported auth) to remote IDEs and AI agents.
- Tune connection limits and idle timeouts to match hosting capacity.

## 12. Next Steps

- Read the [HTTP + SSE transport proposal](../proposals/koan-mcp-http-sse-transport.md) for architectural details and roadmap.
- Explore [Koan MCP Integration](../proposals/koan-mcp-integration.md) to understand how STDIO and upcoming transports align.
- Connect a real IDE or agent using the [TypeScript reference client](../proposals/koan-mcp-http-sse-transport.md#client-side-consumption).

---

**Last Validation**: 2025-01-17 by Framework Specialist \
**Framework Version Tested**: v0.2.18+
