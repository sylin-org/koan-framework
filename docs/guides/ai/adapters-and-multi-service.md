# AI adapters and multi-service routing

This guide summarizes the adapters/registry and router policies for using multiple inference backends (e.g., multiple Ollama instances) locally or in clusters.

- Adapters implement a provider-agnostic contract (AI-0008) and are registered in an AdapterRegistry.
- The Router (AI-0009) selects which adapter to use per request using policies like round-robin, health-aware, sticky by session, etc.
- Controllers expose /ai/capabilities (aggregated), /ai/models, and chat/embedding endpoints. Requests can include headers to influence selection.

Example scenarios
- Two Ollama instances on host: WeightedRoundRobin + HealthAware
- Separate pools by tenant: labels pool=alpha/beta and StickyBySession
- Mixed models: ModelAware directs prompts to appropriate family/size
- Batch embeddings: LeastPending + CapacityGuard; backpressure visible via metrics

Development auto-discovery (Ollama)
- In Development, Koan can probe common Ollama endpoints (localhost:11434, host.docker.internal:11434, and service name ollama:11434) and auto-register reachable instances.
- Controlled by ai:autoDiscovery:enabled (default true in Dev) or env Koan_AI_AUTODISCOVERY; never on by default in Production.
- Discovered entries are labeled discovered=true and pool=dev and appear in /ai/capabilities.

See also
- ADR AI-0008 — adapters and registry: ../../decisions/AI-0008-adapters-and-registry.md
- ADR AI-0009 — multi-service routing: ../../decisions/AI-0009-multi-service-routing-and-policies.md
- Interop matrix: interop-matrix.md
- Native AI Koan — Epic: native-ai-Koan-epic.md
