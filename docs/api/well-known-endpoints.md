# Well-known endpoints

Sora exposes well-known routes for capability discovery and safe observability snapshots.

Capabilities
- Route: `/.well-known/sora/capabilities`
- Purpose: advertise supported capabilities and helpful links.
- Example (simplified):
```
{
  "links": [
    { "rel": "self", "href": "/.well-known/sora/capabilities" },
    { "rel": "observability", "href": "/.well-known/sora/observability" }
  ],
  "capabilities": {
    "pagingPushdown": true,
    "inbox": true,
    "outbox": true
  }
}
```

Observability snapshot
- Route: `/.well-known/sora/observability`
- Purpose: expose safe, non-sensitive runtime status for troubleshooting.
- Example (simplified):
```
{
  "enabled": { "traces": true, "metrics": true },
  "resource": { "service.name": "s2-api", "service.version": "1.0.0" },
  "exporter": { "otlp": { "endpoint": "http://otel-collector:4317" } },
  "currentTraceId": "a1b2c3..."
}
```

Notes
- Exposure is gated by environment or options; do not expose snapshots publicly in production unless intended.
- The client sample proxies these routes and shows a small status card.

References
- docs/decisions/ARCH-0033-opentelemetry-integration.md
