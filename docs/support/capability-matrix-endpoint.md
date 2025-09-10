# Capability Matrix Endpoint (Sora.Web)

Goal: Expose a simple JSON endpoint (via MVC controller) to inspect registered aggregates, their default provider, and declared capabilities.

Route: GET /.well-known/sora/capabilities (configurable via SoraWebOptions in future)

Shape:
{
  "aggregates": [
    {
      "type": "Namespace.Todo",
      "key": "string",
      "provider": "sqlite",
      "query": ["String"],
      "write": ["BulkUpsert", "BulkDelete", "AtomicBatch"]
    }
  ]
}

Source of truth:
- Resolve aggregates known to the DataService/TypeConfigs cache.
- Inspect repository interfaces and optional capability markers (`IStringQueryRepository`, `IBulkUpsert`, `IBulkDelete`, atomic batch support, etc.).

Notes:
- Endpoint is informational and unauthenticated by default; for production, recommend protecting via auth or disabling.
- Provider-specific capabilities may vary by aggregate type; report per aggregate.
