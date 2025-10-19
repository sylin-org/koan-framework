Koan.AI.Connector.LMStudio - Technical reference

> **Contract**  
> Inputs: Koan AI chat/stream/embedding contracts, discovery metadata, orchestration hints.  
> Outputs: OpenAI-compatible HTTP requests, Koan AI responses, readiness metadata, health metrics.  
> Error Modes: HTTP failures, serialization errors, readiness timeouts, missing models/auth.  
> Criteria: Adapter registered via Koan auto-registrar, readiness policy respected, orchestration metadata published.

## Options

- `ConnectionString` (default `auto`) enables host/container discovery.
- `BaseUrl` fallback when discovery fails (defaults to `http://localhost:1234`).
- `ApiKey` optional Bearer credential (`LMSTUDIO_API_KEY` env alias).
- `DefaultModel` used when callers omit `Model`; readiness degrades if absent server-side.
- `RequestTimeoutSeconds` per-request timeout (defaults to 120s).
- `AutoDiscoveryEnabled`, `Weight`, `Labels` surface router metadata for discovered members.
- `Readiness` (policy/timeout/gating) aligns with `AdaptersReadinessOptions` defaults.

## Behavior

- Normalizes base URL to omit trailing `/v1` before issuing relative calls.
- `ChatAsync` posts OpenAI `messages` payloads and merges vendor options onto the root object.
- `StreamAsync` enables SSE, parsing `data:` frames until `[DONE]` and yielding `AiChatChunk` deltas.
- `EmbedAsync` forwards multi-input embedding requests and returns vectors with dimension hints.
- `ListModelsAsync` proxies `/v1/models` to expose adapter model descriptors.
- Readiness pipeline hits `/v1/models` then verifies configured `DefaultModel`; failure downgrades to `Degraded`.
- Health checks record response time, available model count, and exposes base URL to boot report metadata.

## Discovery & orchestration

- Discovery adapter checks (in order): `LMSTUDIO_API_BASE_URL`, `Koan_AI_LMSTUDIO_URLS`, explicit config, host-first loopback, container endpoints, Aspire AppHost service bindings.
- Health validation verifies `/v1/models` responds 2xx and optionally confirms `requiredModel` presence.
- Auto-registrar wires `LMStudioOptions`, discovery adapter, readiness evaluators, and orchestrator metadata.
- Orchestration evaluator contributes container descriptors (image, port 1234, health endpoint `/health`).

## Edge cases

- Missing default model keeps adapter in `Degraded`; router still allows explicit model selection.
- Streaming cancel triggers `OperationCanceledException`; partial tokens already yielded remain valid.
- LM Studio desktop with dynamic ports: set `LMSTUDIO_API_BASE_URL` explicitly to avoid probe failure.
- Auth-enabled servers returning 401 propagate via `HttpRequestException`; ensure key present before readiness.
- Multi-instance discovery: each URL obtains labels/weights from options; duplicates merged by registry.

## References

- ADR: ../../../../docs/decisions/AI-0016-lm-studio-adapter.md
- AI registry ADR: ../../../../docs/decisions/AI-0008-adapters-and-registry.md
- OpenAI surface ADR: ../../../../docs/decisions/AI-0005-protocol-surfaces.md
- Secrets ADR: ../../../../docs/decisions/AI-0004-secrets-provider.md

