# AI-0008 — AI adapters and registry

Status: Proposed
Date: 2025-08-19
Owners: Sora AI

## Context

We want a pluggable AI adapter abstraction so Sora can connect to different yet similar inference offerings (Ollama, OpenAI-compatible, Anthropic/Gemini via shims, and local servers like vLLM/TGI), without leaking provider specifics into application code. The Ollama adapter is the first concrete implementation and must list capabilities, enumerate installed models, and support chat (streaming + non-streaming) and embeddings. We also need a registry to host multiple adapters and expose a unified capabilities view.

## Decision

- Define a narrow, provider-agnostic adapter contract in Sora.AI.Abstractions.
- Provide a default registry in Sora.AI.Core to manage one or many adapters and to aggregate capabilities.
- First-party adapters: Sora.AI.Adapter.Ollama (first), Sora.AI.Adapter.OpenAI (subset). Others can be community.
- Keep controllers as the HTTP surface; adapters are pure services behind controllers.

### Adapter identity and capabilities (contract)

1) Identity
- AdapterId: string (stable, unique within app)
- Kind: enum (Ollama, OpenAI, Anthropic, Gemini, LocalServer, Unknown)
- Endpoint: Uri (or connection descriptor)
- Labels: Dictionary<string,string> for routing (region, tier, pool, model-family, owner)

2) Capabilities (feature flags and metadata)
- SupportsChat: bool
- SupportsStreaming: bool
- SupportsEmbeddings: bool
- SupportsTools: bool (JSON-schema function calling)
- Modalities: ["text", "image:in", "image:out?"]
- MaxContextTokens: int? (per model default)
- MaxOutputTokens: int? (per model default)
- Models: list of ModelDescriptor
  - name (provider id), family, size, defaultTemperature, contextWindow, embeddingDim?
  - quality hints: throughput score, latency class (S/M/L), draft support?
- CostHints: optional pricing/cost unit hints for budgeting (tokens/sec, req/sec, dollars?)
- Version: string (provider/server version)

3) Operations
- ListModelsAsync(cancellation)
- GetCapabilitiesAsync(cancellation)
- ChatAsync(request, cancellation) → ChatResponse (non-stream)
- ChatStreamAsync(request, cancellation) → IAsyncEnumerable<ChatChunk>
- EmbedAsync(request, cancellation) → EmbeddingsResponse
- HealthCheckAsync(cancellation) → AdapterHealth (status, latency, lastError)
- WarmupAsync(optional)

4) Error/consistency
- All operations must map provider errors to a normalized error model and attach ProviderErrorCode in extensions.
- Tokenization numbers may be approximate; expose a TokenizationMethod descriptor per adapter.

5) Configuration
- Each adapter binds strongly-typed Options (e.g., OllamaOptions { BaseUrl, DefaultModel, Timeout, Headers }).
- Secrets are obtained via the AI Secrets provider ADR (AI-0004).

### Registry (AdapterRegistry)

- Responsibilities
  - Register adapters (Add/Replace/Remove) and hold their metadata
  - Aggregate a unified Capabilities document across adapters
  - Provide lookup by AdapterId, Kind, Model name/family
  - Surface health and readiness
- Selection helpers
  - FindAdapterForModel(modelName, hints)
  - FindEmbeddingAdapter(dimensions?, model?)
  - ListAdapters(filters?)
- Thread-safe, supports hot-reload of config and dynamic register/unregister.

### HTTP surface alignment

- The controllers expose:
  - GET /ai/capabilities — merged from the registry
  - GET /ai/models — flattened list with adapterId and model metadata
  - POST /ai/chat and /ai/chat/stream — accept optional adapter/model hints
  - POST /ai/embeddings — accept optional adapter/model hints
- Requests may carry headers to influence selection (e.g., X-Sora-AI-Adapter, X-Sora-AI-Model, X-Sora-AI-RoutePolicy). These map to router policies in AI-0009.

## Consequences

- App code targets abstractions; swapping providers requires config only.
- Ollama adapter will implement model listing via /api/tags (models) and /api/show (metadata) and map to Models/Capabilities.
- The registry becomes the single source for discovery and negotiated capabilities exposed by /ai/capabilities.

## Notes

- Protocol shims (AI-0005) sit above adapters; an OpenAI-compatible shim can delegate to any adapter via the registry.
- Observability: All adapter calls emit OTel spans with attributes: sora.ai.adapter.id, kind, model, operation, tokens.in/out, latency.ms, error.
- Backpressure and multi-service routing is specified in AI-0009.

## Development auto-discovery (Ollama)

Scope: Development only (SoraEnv.IsDevelopment). Controlled by options ai:autoDiscovery:enabled (default true in Dev, false elsewhere). Explicit configuration always wins over discovery.

Heuristics (performed in parallel with short timeouts ≤ 250ms per probe):
- Host endpoints:
  - http://localhost:11434
  - http://127.0.0.1:11434
- From containerized app endpoints:
  - http://host.docker.internal:11434 (Docker Desktop on Windows/macOS; may work on recent Linux)
  - http://ollama:11434 (common Compose service name; relies on Compose DNS)
  - http://gateway.docker.internal:11434 or default gateway (e.g., 172.17.0.1:11434) as a last resort
- Environment hints (highest precedence among discovery):
  - OLLAMA_BASE_URL
  - SORA_AI_OLLAMA_URLS (semicolon/comma-separated)

Probe method: GET /api/tags with 200–500ms overall budget; if reachable, fetch /api/show for a small sample model to refine metadata. Register each reachable endpoint as an Ollama adapter with:
- AdapterId: ollama@{host}:{port}
- Labels: { discovered: "true", pool: "dev", family: inferred-from-model-name }

Safety and controls:
- Only active when SoraEnv.IsDevelopment == true and ai:autoDiscovery:enabled == true
- Can be disabled via env SORA_AI_AUTODISCOVERY=0 or ai:autoDiscovery:enabled=false
- Never runs in Production by default; in non-Dev, explicit configuration is required
- Discovery adds adapters but does not override explicitly-declared adapters with the same AdapterId

DX:
- Log concise discovery summary: candidates tried, successes, and final registry entries
- Expose /ai/capabilities with a "discovered": true flag on auto-added entries

## Configuration model

Binding root: "Sora:Ai" (strongly-typed options). Supports multiple services per provider type via arrays.

appsettings.json shape (illustrative):

Sora:
  Ai:
    AutoDiscovery:
      Enabled: true
      AllowInNonDev: false
      ProbeTimeoutMs: 250
    Health:
      Required: false
    Router:
      DefaultPolicy: "wrw+health+least-pending"  # Dev default; Prod default: "health+least-pending"
      Capacity:
        MaxInflightPerAdapter: 32
        Queue:
          MaxDepth: 128
          Strategy: "fifo"
      Hedging:
        AfterMs: null
      Timeouts:
        ChatMs: 60000
        EmbeddingsMs: 30000
      Weights:
        # adapterId → weight; enables WRR when present
        ollama-a: 2
        ollama-b: 1
    Services:
      Ollama:
        - Id: "ollama-a"
          BaseUrl: "http://host.docker.internal:11434"
          DefaultModel: "llama3.1:8b"
          Weight: 2
          Labels:
            pool: "alpha"
            family: "llama3"
        - Id: "ollama-b"
          BaseUrl: "http://host.docker.internal:11435"
          DefaultModel: "llama3.1:8b"
          Weight: 1
          Labels:
            pool: "beta"
            family: "llama3"
      OpenAI:
        - Id: "openai"
          BaseUrl: "https://api.openai.com/v1"
          ApiKey: "env:OPENAI_API_KEY"   # resolved via AI secrets provider (AI-0004)
          DefaultModel: "gpt-4o-mini"
          Labels:
            tier: "paid"
    Aliases:
      # alias → provider-scoped model names
      "llama3.1:8b": ["ollama:llama3.1:8b"]
      "gpt-lite": ["openai:gpt-4o-mini"]

Strongly-typed options (sketch):
- AiOptions { AutoDiscovery, Health, Router, Services, Aliases }
- ServicesOptions { List<OllamaServiceOptions>, List<OpenAiServiceOptions>, ... }
- OllamaServiceOptions { Id, BaseUrl, DefaultModel?, Weight?, Labels:Dictionary, Headers?, Timeouts?, Enabled? }
- OpenAiServiceOptions { Id, BaseUrl, ApiKey (secret-ref), OrgId?, DefaultModel?, Weight?, Labels, Headers?, Timeouts? }

## Bootstrapping

services.AddSora() auto-wires AI with sane defaults and environment-aware discovery:
- AddSora():
  - Binds Sora:Ai options
  - Calls AddAi(options)
  - Registers providers declared under Sora:Ai:Services:*
  - If none declared and environment is Development (or non-Prod when configured), runs auto-discovery for Ollama and registers any found endpoints
  - Adds routing automatically; if only one eligible provider is present, IAi bypasses the router

Manual opt-in remains supported:
- services.AddAi().AddOllama("ollama-a", url).AddRouting();

Public API for callers remains IAi (PromptAsync/StreamAsync/EmbedAsync) and the HTTP controllers under /ai/*.

## Persistence

The AI runtime may persist internal state using Sora’s Entity patterns on the default database or a configured connection:
- Entities (examples): AiServiceState (health snapshots), AiCallRecord (request/response metadata), AiBudget (quota usage), AiRouteDecision (audit)
- Options: Sora:Ai:Persistence { Enabled: false, ConnectionName: null, Schema/TablePrefix: "Ai" }
- Persistence is off by default; when enabled, it augments telemetry with durable audit trails and quota enforcement.
