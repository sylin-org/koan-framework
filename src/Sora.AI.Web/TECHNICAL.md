Sora.AI.Web — Technical reference

Contract
- Inputs: HTTP requests to AI endpoints (controller-driven only), validated DTOs.
- Outputs: JSON responses and server-sent events for streaming.
- Success: 2xx with payload; Errors: 4xx for validation/auth, 5xx for provider/runtime.

Architecture
- Expose endpoints via MVC controllers (no inline MapGet/MapPost) per WEB-0035.
- Depends on Sora.AI and Sora.AI.Contracts abstractions; does not couple to a specific provider.
- Authentication/authorization integrates with Sora.Web.Auth when enabled.

Options
- Route bases, response buffering/streaming thresholds, max request size, timeouts.
- CORS and caching policies configured via typed options; no scattered literals (ARCH-0040).

Error modes
- 400: invalid DTO, unsupported model; 401/403: auth failures; 408: timeout; 429: rate limited; 5xx: provider/backing service errors.

Edge cases
- Large payloads; client disconnects mid-stream; slow consumers; SSE retry behavior.
- Concurrency limits and graceful shutdown honoring CancellationToken.

Operations
- Health endpoints and dependency probes; structured logging with correlation id.
- Backpressure via server limits and per-endpoint quotas.

References
- ./README.md
- /docs/api/web-http-api.md
- /docs/engineering/index.md