# Well-known endpoints

Koan exposes well-known routes for capability discovery and safe observability snapshots.

## Matrix (quick)
- Path: `/.well-known/Koan/capabilities` — Method: GET — Purpose: advertised capabilities and links — Auth: typically anonymous in Dev, gated in Prod.
- Path: `/.well-known/Koan/observability` — Method: GET — Purpose: safe observability snapshot — Auth: gated (never expose sensitive data).
- Path: `/.well-known/auth/providers` — Method: GET — Purpose: auth provider discovery — Auth: typically anonymous.

Capabilities
- Route: `/.well-known/Koan/capabilities`
- Purpose: advertise supported capabilities and helpful links.
- Example (simplified):
```
{
  "links": [
    { "rel": "self", "href": "/.well-known/Koan/capabilities" },
    { "rel": "observability", "href": "/.well-known/Koan/observability" }
  ],
  "capabilities": {
    "pagingPushdown": true,
    "inbox": true,
    "outbox": true
  }
}
```

Observability snapshot
- Route: `/.well-known/Koan/observability`
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
- Authentication discovery is covered by Koan.Web.Auth; see also `/auth/{provider}/challenge`, `/auth/{provider}/callback`, and `/auth/logout`.

References
- docs/decisions/ARCH-0033-opentelemetry-integration.md
