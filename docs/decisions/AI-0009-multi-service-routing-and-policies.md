# AI-0009 — Multi-service routing, load balancing, and policies

Status: Proposed
Date: 2025-08-19
Owners: Sora AI

## Context

Users may run multiple inference backends of the same kind (e.g., two or more Ollama instances) or a mix of providers. We need a simple yet powerful router layered above the AdapterRegistry to support load balancing, failover, task distribution, quotas, and governance while preserving SSE semantics and Sora’s controller-first model.

## Decision

Introduce an AI Router in Sora.AI.Core with pluggable policies. It selects an adapter/endpoint per request using model hints, labels, health, and policy inputs. It exposes selection results to controllers and observability.

### Router contract
- SelectAsync(intent, requestMetadata, cancellation) → RouteDecision
  - intent: Chat, ChatStream, Embeddings
  - requestMetadata: model hint, tenant, budget, latency preference, maxTokens
  - RouteDecision: selected AdapterId, Model, PolicyApplied, Reasons[], StickyKey?, Hedged? (bool)
- Execute helpers wrap adapter calls to apply timeouts, hedging, and streaming guarantees.

### Built-in policies
- RoundRobin (per adapter pool)
- WeightedRoundRobin (weights via config/labels)
- LeastPending (pending inflight counter per adapter)
- HealthAware (skip degraded/out adapters; e.g., last failure, latency p95)
- StickyBySession (hash on session/user/tenant)
- ModelAware (route by model family or name; can rewrite model alias to provider-specific name)
- CostAware (prefer cheaper adapters if budget constrained; tie-break by health)
- CapacityGuard (max inflight per adapter; queue or reject with 429/503)
- Failover (fallback chain; primary→secondary)
- Hedging (duplicate after T ms; cancel loser on first success)

### Headers and options
- X-Sora-AI-Adapter: force a specific adapter id (admins only)
- X-Sora-AI-Model: model hint or alias (e.g., llama3.1:8b)
- X-Sora-AI-RoutePolicy: named policy (rr, wrw, least-pending, sticky:session)
- X-Sora-AI-StickyKey: override sticky key
- X-Sora-AI-Budget: tokens/$ or request budget hint
- X-Sora-AI-Timeout: per-request timeout

### Auto-wire and default flow
- Auto-wire: If the AdapterRegistry contains 2+ eligible adapters for the intent/model, the Router is engaged automatically; controllers delegate selection with no extra setup.
- Sane defaults:
  - Development: default policy = WeightedRoundRobin + HealthAware with LeastPending as a tiebreaker; discovered adapters default weight=1.
  - Production: default policy = HealthAware + LeastPending; WeightedRoundRobin is applied only when weights are configured.
- In-flight accounting: The Router tracks per-adapter inflight counts (and optionally estimated tokens). LeastPending steers successive requests away from currently busy adapters.
  - Example: Two back-to-back prompts → first routes to A (inflight A=1), second routes to B (inflight B=1), subsequent prompts follow policy rules as inflight changes.
- Streaming awareness: An adapter stays “busy” while SSE streaming is active (counts in inflight); completion/abort decrements inflight.
- Admission control: If CapacityGuard limits are reached, requests are queued up to queue.maxDepth, otherwise 429/503 is returned with a Retry-After hint.

### Health, metrics, and backpressure
- Router maintains per-adapter health: success/failure counts, EWMA latency, last error, circuit breaker state
- Expose /ai/health and include router status in /ai/capabilities
- CapacityGuard rejects or queues when adapters exceed inflight; queue depth is observable
- SSE streaming: ensure monotonic chunk ordering; on hedging, only one stream is surfaced

#### Overall AI health and core telemetry wiring
- Engagement: If the registry has ≥ 1 adapter, the AI subsystem is considered engaged and contributes to app health (see ARCH-0013).
- Rollup states (component: "ai"):
  - Healthy: all registered adapters report Healthy
  - Degraded: at least one adapter is Unhealthy or Degraded, but at least one adapter remains Healthy
  - Unhealthy: no adapters are Healthy (all unavailable/degraded beyond threshold)
  - Inactive: no adapters registered (component not required unless configured as required)
- Required vs optional:
  - By default AI is optional for overall app readiness; set ai:health:required=true to propagate Degraded/Unhealthy to the app readiness result.
- Telemetry (OTel):
  - Metrics (gauges): sora.ai.adapters.total, .healthy, .degraded, .unhealthy, .inflight, .queue.depth
  - Metrics (histograms): sora.ai.latency.ms, sora.ai.tokens.in, sora.ai.tokens.out
  - Spans: attributes sora.ai.adapter.id, kind, model, policy, route.decision, health.state
  - Events on state change: ai.health.changed from→to including reasons (last error, circuit-breaker)
- Health endpoints:
  - GET /ai/health returns { overall, adapters[], router { status, policy, inflight, queue } }
  - App-level health surfaces AI as a component entry aligned to ARCH-0013 health announcements.

### Multi-Ollama scenarios
- Two Ollama instances with identical models: use WeightedRoundRobin + HealthAware for smooth load
- Heterogeneous pool (8B + 3B): ModelAware routes large prompts to 8B, short to 3B
- Tenant isolation: Labels pool=alpha/beta; StickyBySession keeps users on their pool
- Batch embedding jobs: LeastPending + CapacityGuard; optional backoff/retry on 429

### Configuration and overrides
- Router options (excerpt):
  - router.defaultPolicy: rr | wrw | least-pending | sticky:session | health+least-pending (composable)
  - router.capacity.maxInflightPerAdapter: int (default 32)
  - router.capacity.queue.maxDepth: int (default 128), queue.strategy: fifo | lifo
  - router.hedging.afterMs: int? (disabled by default), router.timeouts.chatMs/embeddingsMs
  - router.weights[adapterId]: int (if set, enables WRR in Dev; explicit only in Prod)
- Headers can hint per-request routing (see above); admin-only headers may be rejected for non-admin callers.

### Recommended namespaces and API shape
- Namespaces (terse, semantic):
  - Sora.AI — root abstractions and DI entrypoints (AddAi)
  - Sora.AI.Contracts — request/response DTOs and interfaces (IAi, IAiRouter)
  - Sora.AI.Runtime — runtime services (Router, Health, Inflight, Policies)
  - Sora.AI.Catalog — provider registry and model catalog
  - Sora.AI.Providers.Ollama — Ollama provider (HTTP client, mapping)
  - Sora.AI.AspNet — MVC controllers and wiring
- Primary entrypoint:
  - IAi with PromptAsync(request), StreamAsync(request), and EmbedAsync(request)
  - IAi internally consults IAiRouter when 2+ eligible providers exist; otherwise calls the single provider directly
- DI surface:
  - services.AddAi(o => { o.AutoDiscovery = DevDefault; o.DefaultPolicy = DevDefault; })
      .AddOllama("ollama-a", url)
      .AddOllama("ollama-b", url2)
      .AddRouting();
  - Or via top-level:
    - services.AddSora(); // Auto-binds Sora:Ai, registers declared services, runs Dev auto-discovery if none declared, and self-registers routing

### Minimal flows (behavioral)
1) Single provider, no routing
   - Setup: services.AddAi().AddOllama("ollama-a", url);
   - Behavior: IAi.PromptAsync("Hey") sends all prompts to ollama-a; router stays inactive.
2) Two providers, routing auto-engaged
   - Setup: services.AddAi().AddOllama("ollama-a", url).AddOllama("ollama-b", url2).AddRouting();
   - Behavior: IAi.PromptAsync("Hey") remains the only entry point. Router selects per policy:
     - Back-to-back prompts: first → A, second → B (due to LeastPending/WRR), then follow policy as load/health changes.
   - No user-facing change in API or endpoints; only distribution differs.

## Consequences

- Clear separation: Adapters do provider I/O; Registry discovers; Router decides and enforces policies
- Governance hooks: per-tenant rate limits and budgets can decorate router decisions
- Failures become visible and recoverable (circuit breaking, hedging)

## Notes

- Router integrates with secrets (AI-0004) and protocol surfaces (AI-0005). OpenAI shim requests can pass through the same policies.
- SSE SLOs and backpressure guidelines remain as in AI-0002.

### Development auto-discovery interplay
- Auto-discovered adapters (see AI-0008) are labeled discovered=true and pool=dev by default.
- Default policy in Development: WeightedRoundRobin + HealthAware across explicit + discovered adapters.
- Production safety: In non-Development, discovered adapters are ignored unless ai:autoDiscovery:allowInNonDev=true is set.
- Weights: discovered adapters default weight=1; explicit adapters can override weight; health still governs selection.
