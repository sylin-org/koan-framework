# Sylin.Koan.Mcp.Explorer

A human console over the exact MCP surface an agent sees. Explorer shows caller-visible Entity and custom tools,
explains doors without revealing walls, supports governed in-process try-it, and offers a separately protected
operator access map.

## Install and use

```powershell
dotnet add package Sylin.Koan.Mcp.Explorer
```

Keep the normal Koan host:

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();
```

The package reference activates Explorer through its `KoanModule`; no Explorer registration or endpoint mapping is
required. In Development outside a container, browse the MCP base route—`/mcp` by default.

## Meaningful result

Explorer projects four related views from MCP's existing registries and executor:

| Route | Meaning |
|---|---|
| `GET /mcp` with browser HTML accept | Embedded, offline-capable Explorer console. |
| `GET /mcp/map.json` | The current caller's usable tools plus disclosed doors; walls remain absent. |
| `POST /mcp/explorer/call` | Invoke a tool in-process as the authenticated caller. |
| `GET /mcp/access-map.json` | Privileged full requirement map, including walls. |

Static assets live below `/mcp/explorer/`. The base follows `Koan:Mcp:HttpRoute`.

## Production posture

Explorer defaults off in production and containers. Enable it deliberately and choose at least one access-map gate:

```json
{
  "Koan": {
    "Mcp": {
      "Explorer": {
        "Enabled": true,
        "AdminRole": "mcp-operator"
      }
    }
  }
}
```

`AdminScope` is the scope-based alternative to `AdminRole`. The caller-specific map remains safe for anonymous
description; try-it requires an authenticated caller. The privileged access map is Development-only unless one of
the configured admin gates succeeds, and returns 404 otherwise.

## Guarantees and boundaries

- Explorer does not own a second tool registry, schema, authorization policy, or executor. Description and try-it use
  MCP Core's caller-aware projection and governed execution path.
- The console is embedded in the package and uses no CDN. Asset paths reject traversal and responses are `no-store`.
- Browser content negotiation never replaces the MCP stream response for an MCP client.
- Enabling Explorer is not authentication setup. Compose an ASP.NET Core/Koan authentication mechanism before
  exposing try-it or privileged diagnostics beyond a trusted Development host.
- The access map deliberately contains authorization requirements. Keep it operator-only; do not treat its 404
  posture as the sole network security boundary.

See [TECHNICAL.md](TECHNICAL.md) for route ownership and enforcement details.
