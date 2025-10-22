---
type: GUIDE
domain: web
title: "Pantry API and MCP (S16)"
audience: [developers, architects]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-09
  status: verified
  scope: docs/guides/web/pantry-api-and-mcp.md
---

# Pantry API and MCP (S16)

## Contract

- Inputs: Koan Web app with `AddKoan()`, Pantry entity models, Docker/Compose for API and MCP services
- Outputs: HTTP API on port 5016 and MCP endpoint on 5026 using controller patterns and entity statics
- Error modes: Wrong compose contexts/Dockerfiles, probing Swagger instead of root, orphaned containers
- Success criteria: Stack starts via `API/start.bat`, root probe passes, minimal GET routes work, MCP SDK endpoint responds

### Edge cases

- Probing: Use root URL `/` instead of Swagger to avoid flakiness
- Orphans: Use `--remove-orphans` when bringing the stack up during iteration
- Data size: Expose explicit paging for lists; use streaming for jobs/exports

---

## Try it

1) From `samples/S16.PantryPal/API`, start the stack:

```powershell
./start.bat
```

2) Smoke tests:

```powershell
# API root (readiness)
curl http://localhost:5016/

# MCP SDK definitions
curl http://localhost:5026/mcp/sdk/definitions
```

---

## Minimal patterns

- Program: `builder.Services.AddKoan()` (keep bootstrap minimal)
- Controllers: inherit `EntityController<T>` (no inline endpoints)
- Data access: `Entity<T>` statics (All/Query/FirstPage/Page/AllStream/QueryStream)

```csharp
[Route("api/[controller]")]
public class ItemsController : EntityController<Item> { }
```

## Paging & streaming

```csharp
// Controller: prefer explicit paging for client endpoints
public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default)
{
    var result = await Item.Page(page, size, ct);
    return Ok(result);
}

// Jobs: stream large sets
await foreach (var it in Item.AllStream(batchSize: 500, ct)) { /* process */ }
```

---

## Related

- Reference: Web HTTP API; Entity controller and transformers; Pagination attribute
- Samples: `samples/S16.PantryPal`
