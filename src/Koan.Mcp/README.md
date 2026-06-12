# Koan.Mcp

## Contract
- **Purpose**: Implement a Model Context Protocol (MCP) host for Koan modules, exposing entities, tools, and diagnostics to AI-powered agents.
- **Primary inputs**: `McpEntityAttribute`-decorated types, entity registries built from `McpEntityRegistration`, Koan adapters describing available capabilities.
- **Outputs**: MCP descriptors composed via `DescriptorMapper`, hosted endpoints through the Koan hosting layer, and tool definitions consumable by MCP clients.
- **Failure modes**: Missing entity annotations, unsupported transport modes, or MCP schema mismatches when serializing descriptors.
- **Success criteria**: MCP clients can enumerate Koan entities/tools, execute actions through the protocol, and receive diagnostics in the expected schema.

## Quick start

To expose a Koan entity over the Model Context Protocol (MCP), simply annotate your entity class with the `[McpEntity]` attribute. The framework's Zero-DX scanner will automatically discover the entity, map its relational/data endpoints to MCP tools, and host them.

### 1. Annotate the Entity

Decorate your entity class with `[McpEntity]`. Set `AllowMutations = false` if you want to expose a safe, read-only surface (collection listing, query, and get-by-id) without leaking modification tools.

```csharp
using Koan.Mcp;
using Koan.Data.Core.Model;

namespace MyApp;

[McpEntity(Name = "Product", Description = "Public product catalog", AllowMutations = false)]
public sealed class Product : Entity<Product>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
```

### 2. Configure and Map Endpoints

In your `Program.cs`, register the core Koan, Web, and MCP services, then call `MapKoanMcpEndpoints()` to expose the protocol.

```csharp
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp;
using Koan.Mcp.Extensions;
using Koan.Mcp.Options;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register Koan core, Web, and MCP services
builder.Services.AddKoan().AsProxiedApi();
builder.Services.AddKoanWeb();
builder.Services.AddKoanMcp();

// Configure MCP server options (e.g., enable HTTP+SSE transport)
builder.Services.Configure<McpServerOptions>(o =>
{
    o.Exposure = McpExposureMode.Full;
    o.EnableHttpSseTransport = true;
    o.HttpSseRoute = "/mcp";
});

var app = builder.Build();

// Initialize the static service locator
AppHost.Current = app.Services;

// Map the MCP endpoints (SSE stream and RPC endpoints)
app.MapKoanMcpEndpoints();

await app.RunAsync();
```

### 3. Consume via MCP Client

The application hosts the MCP SSE transport at:
- **SSE Handshake Stream**: `GET /mcp/sse` (initiates session, returns header `X-Mcp-Session: <id>` and event `endpoint`)
- **POST RPC Endpoint**: `POST /mcp/rpc?sessionId=<id>` (receives client requests and executes tools)

A standard MCP client can query the tools using standard protocol envelopes:
- `tools/list`: Enumerate available tools (`product.collection`, `product.get-by-id`).
- `tools/call`: Execute a tool (e.g., `product.collection` with query arguments).

## Safe Public Exposure & Auth Posture

When exposing MCP to public networks, adopt the following security guidelines:
1. **Disable mutations**: Always set `AllowMutations = false` on exposed entities. This prevents any data-modification tools (`upsert`, `delete`, `patch`) from being registered or executed.
2. **Require Authentication**: By default, endpoints mapped via `MapKoanMcpEndpoints()` respect host auth policies. To force authentication, configure `McpServerOptions`:
   ```csharp
   builder.Services.Configure<McpServerOptions>(o =>
   {
       o.RequireAuthentication = true;
   });
   ```
   This gates the SSE and RPC endpoints behind the ASP.NET Core authorization middleware. For public read-only access, leave `RequireAuthentication = false` but ensure the underlying models are strictly read-only (`AllowMutations = false`).

## Related packages
- `Koan.Core` – DI patterns, boot reporting, and environment helpers.
- `Koan.Data.Core` – provides entity paging/streaming and repository facades.
- `Koan.Core.Adapters` – adapter discovery feeding into MCP tool projection.

## Reference
- `McpEntityAttribute` – decorator to mark entities for MCP exposure.
- `DescriptorMapper` – maps data descriptors to MCP-compliant schemas and tools.
- `McpRpcHandler` – handles incoming JSON-RPC calls for tool discovery and execution.

