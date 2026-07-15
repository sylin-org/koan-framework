# Koan.Mcp

## Contract

- **Purpose**: Project governed Koan entities, business tools, and runtime explanation to AI agents.
- **Primary inputs**: referenced `Sylin.Koan.Mcp`, `[McpEntity]` entities, optional `[McpTool]`
  workflows, and the application's existing access declarations.
- **Outputs**: caller-specific MCP tools and resources over STDIO or Streamable HTTP.
- **Failure modes**: no annotated surface, invalid JSON-RPC, unavailable tools, denied access, or an
  unsupported transport request.
- **Success criteria**: an agent discovers only usable capabilities, receives structured failures,
  and can inspect the same runtime facts as an operator.

## Shortest supported path

The repository currently demonstrates this contract from source and from staged clean-room
artifacts; the public 0.17.0 package set is not a coherent install path. Add `Koan.Mcp` to the
application closure (`Sylin.Koan.Mcp` is the package identity), annotate the entity, and keep the
normal Koan bootstrap:

```csharp
using Koan.Data.Core.Model;
using Koan.Mcp;

[McpEntity(Name = "product", Description = "A product in the public catalog", AllowMutations = false)]
public sealed class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
```

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

The package reference is the registration. Do not add `AddKoanMcp()`, manually map protocol
endpoints, or assign `AppHost.Current` in application code.

STDIO is enabled by default. To add the HTTP transport in an application that also references
Koan Web, declare the intent in configuration:

```json
{
  "Koan": {
    "Mcp": {
      "EnableHttpSseTransport": true,
      "Exposure": "Tools"
    }
  }
}
```

The historical option name is retained for compatibility; enabling it selects modern Streamable
HTTP by default. The deprecated two-endpoint SSE transport remains off unless
`EnableLegacySseTransport` is explicitly enabled.

## Streamable HTTP

The default base route is `/mcp`:

| Request | Meaning |
|---|---|
| `POST /mcp` | Send one JSON-RPC message, including the initial `initialize` request and tool calls. |
| `GET /mcp` | Open the optional resumable server-push stream for an established session. It is not the initialization handshake. |
| `DELETE /mcp` | Terminate the session named by `Mcp-Session-Id`. |

The first `POST /mcp` carries `initialize` without a session header. Its response mints
`Mcp-Session-Id`; echo that header on later requests and send the negotiated
`MCP-Protocol-Version`. A normal MCP client performs this negotiation—application developers do not
write JSON-RPC handlers.

The old `GET /mcp/sse` plus `POST /mcp/rpc` shape exists only when the deprecated legacy transport
is explicitly enabled.

## Governance

`AllowMutations = false` removes generated mutation tools, but it is not an authorization system.
Use Koan's entity `[Access]` declarations for data authority and tool access policies/scopes for
custom workflows. HTTP authentication is required by default in production and containers; local
Development may be open so the first-use path remains observable.

Tool advertisement and enforcement share the same caller-aware projection. A denied or disabled
tool is not offered and cannot be reached by calling its name directly.

Set `IsMutation = true` on a mutating `[McpTool]`. Its input schema advertises `dry_run`; because
arbitrary imperative effects are not inspectable, `dry_run: true` returns a non-executing partial
rehearsal that names that boundary. Generated Entity mutations return framework-owned prospective
deltas. Convention-based Entity property names are valid fallback schema descriptions and do not
produce startup warnings; add `[McpDescription]` only when richer agent guidance adds value.

## Runtime inspection

- `koan://self` introduces the caller-visible Entity and custom-workflow surface.
- `koan://entities` remains Entity-specific and can honestly be empty in a custom-tool-only app.
- `koan://facts` returns the same versioned, redacted runtime-fact envelope as startup, health, and
  `/.well-known/Koan/facts`. Check `complete` and branch on stable fact codes rather than prose.

## Related packages

- `Koan.Core` — composition, startup reporting, and runtime facts.
- `Koan.Data.Core` — Entity persistence and query semantics.
- `Koan.Web` — governed REST and Streamable HTTP hosting.
- `Koan.Mcp.Explorer` — an optional human inspection surface over the same projection.

See [TECHNICAL.md](TECHNICAL.md) for composition and transport details.
