# AI-0010 — Prompt entrypoint and augmentation pipeline

Status: Accepted
Date: 2025-08-19
Owners: Sora AI

## Context

Sora needs a single, simple entrypoint for user prompts that remains stable whether a single provider or multiple routed providers are configured. We also need a first-class augmentation model (RAG, system prompts, moderation, tools, budgeting) that composes cleanly, is testable, and preserves SSE semantics.

## Decision

Provide a single interface (IAi) as the application-facing entrypoint with typed requests and optional streaming. Introduce an augmentation pipeline with well-defined phases and DI-driven composition. Offer an optional static facade (Sora.Ai) that forwards to DI for ergonomic one-liners without bypassing lifetimes.

### Entrypoint interface (IAi)
- Methods
  - Task<AiChatResponse> PromptAsync(AiChatRequest request, CancellationToken ct = default)
  - IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, CancellationToken ct = default)
  - Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
- Convenience overloads
  - Task<string> PromptAsync(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
  - IAsyncEnumerable<AiChatChunk> StreamAsync(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
- Behavior
  - If one eligible provider exists, IAi calls it directly; if 2+, IAi consults IAiRouter (AI-0009).
  - SSE semantics preserved in StreamAsync; chunks are monotonic and carry metadata (tokens, model, adapterId).

Decision summary
- Adopt IAi as the canonical entrypoint for chat/stream/embeddings.
- Adopt an optional static facade Sora.Ai that resolves IAi via DI (with AsyncLocal override for tests).
- Adopt a modular augmentation pipeline with the phases listed below.

### Core DTOs
- AiChatRequest
  - Messages: List<AiMessage> (role: system|user|assistant|tool; content parts: text|image|json)
  - Model: string? (alias or provider name)
  - Options: AiPromptOptions (temperature?, maxOutputTokens?, topP?, stop?, seed?, profile?)
  - Route: AiRouteHints (adapterId?, policy?, stickyKey?)
  - Tools: List<AiTool>? (JSON-schema tools)
  - Context: Dictionary<string, string>? (free-form hints for augmentations)
- AiChatResponse
  - Text, FinishReason, TokensIn, TokensOut, Model, AdapterId, ToolCalls?, Citations?
- AiChatChunk
  - DeltaText, Index, Model, AdapterId, TokensOutInc, ToolCallDelta?, CitationDelta?
- AiEmbeddingsRequest { Input: string[]|AiMessage[], Model?: string }
- AiEmbeddingsResponse { Vectors: float[][], Model, Dimension }

### Augmentation pipeline
- Phases and hooks (interfaces)
  - IAugmentation.OnPrepareAsync(AiContext ctx)  // before routing (e.g., RAG retrieval, system prompt compose)
  - IAugmentation.OnBeforeCallAsync(AiContext ctx)  // after route decision, before provider call (e.g., tool injection)
  - IAugmentation.OnChunkAsync(AiContext ctx, AiChatChunk chunk, IChunkWriter next)  // stream tap; may annotate
  - IAugmentation.OnAfterCallAsync(AiContext ctx, AiChatResponse response)  // post-process, redact, summarize
- Composition
  - Ordered by registration (or priority); each hook can be no-op; streaming path uses a light-weight chain to keep latency low.
  - Augmentations MUST avoid mutating Provider responses in a way that breaks determinism; annotations go to extensions/citations.

### Built-in augmentations (initial set)
- RagAugmentation — chunk+retrieve, attach context and citations
- SystemPromptAugmentation — apply profiles (e.g., "default", "secure") and project policies
- ModerationAugmentation — pre/post moderation, redact/deny on policy
- HistoryAugmentation — ephemeral or Redis-backed short history
- ToolsAugmentation — map JSON-schema tools to provider-native calls or tool routing
- BudgetGuardAugmentation — enforce budgets (tokens/$) with graceful fail
- RedactionAugmentation — hash redaction for sensitive fields

### Configuration and usage
- DI
  - services.AddSora();  // auto-wires AddAi, providers, router, and reads Sora:Ai config
  - services.AddAi().UseRag().UseSystemPrompt("default").UseModeration().UseTools();
- Per-request
  - AiPromptOptions { Profile?: string, Use?: string[] (e.g., ["rag","moderation"]) }
  - Headers map to Route hints and profiles when using HTTP controllers.

## Consequences

- Single entrypoint simplifies usage: IAi.PromptAsync/StreamAsync works the same with 1 or many providers.
- Augmentations are composable, testable, and keep controllers thin.
- SSE behavior is preserved; stream taps allow lightweight annotations/citations without breaking order.

Rationale
- A single entrypoint keeps usage stable as backends and routing change; augmentations remain composable and testable.
- The static facade provides memorability (Sora.Ai.Prompt) while preserving DI lifetimes and testability.

Alternatives considered
- Only DI (no static facade): maximally pure but less ergonomic for top-level/UI code.
- Global singleton IAi: breaks lifetimes/testability; rejected.
- Inline middleware-only augmentations: harder to reuse/test across transports; kept augmentations in a dedicated pipeline instead.

Versioning and compatibility
- DTOs are additive-forward: new fields optional; method signatures stable. Breaking changes require a new minor with migration notes.

## Notes

- Contracts align with AI-0002 (API and SSE); router flow aligns with AI-0009.
- Telemetry: IAi emits spans around augmentations and provider calls with tokens/latency.

## Optional static facade: Sora.Ai

To provide a mnemonic one-liner, expose a thin, stateless static facade that forwards to the DI-provisioned IAi.

API (facade)
- class Sora.Ai
  - static Task<AiChatResponse> Prompt(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
  - static IAsyncEnumerable<AiChatChunk> Stream(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
  - static Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)

Resolution and scoping
- Internally resolves IAi from SoraEnv.ServiceProvider (see ARCH-0039).
- Uses the current scope when available (ASP.NET request); otherwise creates a short-lived scope.
- Throws an instructive InvalidOperationException if IAi isn’t registered (e.g., “AI not configured; call services.AddSora() or AddAi()”).

Overrides and testing
- Provide an AsyncLocal<IAi?> override used when set (highest precedence) for tests or ad-hoc scenarios.
- Helpers: using Sora.Ai.With(IAi custom) to set override within a disposable scope; do not expose global mutable singletons.
- Guidance: prefer constructor-injected IAi in libraries/services; use Sora.Ai facade in app/UI code for ergonomics.

Performance and safety
- Stateless facade; resolves IAi via delegate cached per ServiceProvider instance to minimize overhead.
- Cancellation tokens flow through; no internal buffering beyond what augmentations/provider require.
- No hidden state; honors DI lifetimes and ambient request scope.

Example
- var text = (await Sora.Ai.Prompt("Hey!", model: "llama3.1:8b")).Text;
- await foreach (var chunk in Sora.Ai.Stream("Explain RAG simply")) { /* write to SSE */ }
