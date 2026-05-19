# AI-0035 - URL override for ad-hoc inference targeting

**Status:** Proposed, 2026-05-18

> Allow a caller to send one AI request to a specific URL without registering a source first. For callers that own the routing concerns themselves (admin-managed inference pools, per-call experimentation), the source/member registry is overhead, not infrastructure.

## Context

Koan AI's routing model ([ADR-0015 Source-Member](AI-0007-inference-servers-interop.md), [AI-0009 multi-service routing](AI-0009-multi-service-routing-and-policies.md)) is config-time: sources and members are populated from `Koan:Ai:Sources:*` (explicit) or auto-discovery at startup, then resolved per request via name (`ChatOptions.Source`, `AiRouteHints.AdapterId`). The framework owns health, circuit breakers, fallback policies, and capability matching on behalf of the caller.

A consuming application has surfaced a use case where this indirection is friction: an **admin-managed inference pool** persisted in the application's domain layer, with per-row enable/disable, capability tracking (installed models, VRAM hints), and runtime add/remove via an admin UI. The application has its own routing layer that selects an inference server per call based on its domain rules; what it needs from Koan is the HTTP+JSON executor for the chosen URL, not another resolution layer on top.

Today the caller has two options, neither clean:

1. **Synchronize their domain into Koan's source registry** via `IAiSourceRegistry.RegisterSource(...)`. Requires a sync layer (watch the entity collection, push membership/health changes into the registry, deal with stale-cache windows). Doubles the source of truth.
2. **Bypass Koan AI entirely** and write a direct HTTP client for the provider's chat endpoint. Loses the fluent `AiConversationBuilder` surface, the message envelope, prompt builder integration, and any future cross-cutting concerns the framework adds.

Both options are working code, but both leave the caller fighting an abstraction that doesn't fit their actual model.

The plumbing for the desired behaviour is already in the framework. Every request type already carries `InternalConnectionString` ([ADR-0015](AI-0007-inference-servers-interop.md)) — the field the router writes to inject the resolved member URL into the adapter. Adapters read it directly:

```csharp
// OllamaAdapter.cs
var http = GetHttpClientForRequest(request.InternalConnectionString);
```

The mechanism for "send this chat call to that specific URL" is wired end-to-end. The only thing missing is a public surface for the caller to use it.

## Decision

### D1. Public URL override on the request

`AiChatRequest` gains two new init-only fields:

```csharp
public string? OverrideUrl { get; init; }
public string? OverrideProvider { get; init; }
```

When `OverrideUrl` is non-empty, the router bypasses source/member resolution and synthesizes a transient resolution carrying the URL through to the adapter via the existing `InternalConnectionString` plumbing. `OverrideProvider` selects which adapter handles the call (defaults to `"ollama"` when null).

The existing internal field name (`InternalConnectionString`) is preserved unchanged — its semantic role hasn't shifted, it's still the channel the adapter reads from. Only the *source* of that string changed: it may now originate from a caller-supplied override rather than a registry-resolved member.

### D2. Router short-circuit

`AiCategoryRouter.ResolveChat` checks for the override at the top of the method:

```csharp
if (!string.IsNullOrWhiteSpace(request.OverrideUrl))
{
    return SynthesizeOverrideResolution(
        url: request.OverrideUrl!,
        provider: request.OverrideProvider,
        category: AiCapability.Chat,
        model: request.Model);
}
```

The synthetic resolution constructs ephemeral `AiSourceDefinition` + `AiMemberDefinition` instances with `Origin = "url-override"` for diagnostics. The adapter is resolved from `IAiAdapterRegistry.Get(provider)` — i.e., the provider must still have its adapter registered, even though no source is registered with it.

When the requested provider has no registered adapter, the router throws with a clear error message listing the available adapter ids.

### D3. Fluent surface on `AiConversationBuilder`

A new method `WithUrl(string url, string provider = "ollama")`:

```csharp
await Client.Conversation()
    .WithUrl("http://stone-indigo-nave:11434", "ollama")
    .WithModel("qwen3:32b")
    .WithSystem(systemDirective)
    .WithUser(userMessage)
    .Send(ct);
```

Setting the URL is idempotent and may be re-set up to `Build()` time. Combining `WithUrl` with `WithRouteAdapter` is allowed — the URL override takes precedence and the route hint is ignored (logged at debug level when both are set, in case a caller accidentally double-specified).

### D4. What the caller gives up

A request that goes through URL override skips, on purpose:

- **Source-level circuit breaker.** Repeated failures against the URL don't accumulate into a tripped circuit; the next call still attempts the same URL. The caller's domain layer owns this concern.
- **Member health monitoring.** Koan's probe service does not check the URL; it's not registered as a member.
- **Fallback.** No "try the next member when this one fails" behaviour; failures surface directly to the caller.
- **Source-level policy.** `Fallback` / `RoundRobin` / `WeightedRoundRobin` apply to source members; URL overrides don't participate.
- **Capability matching.** The router skips the "find a source advertising capability X" filter; the caller assures the URL supports the request.

These are deliberate trade-offs. The caller-owned routing model assumes the caller already tracks health and capabilities in its own storage and applies its own enable/disable rules before calling. Koan's job in this mode is the protocol-level executor, nothing more.

### D5. Scope: chat first

This ADR applies to `AiChatRequest` only. The other request types (`AiEmbeddingsRequest`, `OcrRequest`, `RerankRequest`, etc.) also carry `InternalConnectionString` and would accept the same treatment. Extension to those is deferred until a concrete consumer use case lands; the architecture is identical so the extension is mechanical.

## Consequences

### Positive

- **One additional public method** (`WithUrl`), two additional optional request fields, one router short-circuit. ~60 LOC total. The change is purely additive — no existing caller is affected.
- **Caller-owned routing model** is supported as a first-class option without requiring the caller to fight or bypass framework infrastructure.
- **Plumbing reuse**: the adapter side is unchanged. `InternalConnectionString` semantics stay the same; only the source of that value broadens to include caller overrides. No adapter (Ollama, LMStudio, …) needs an update.
- **Diagnostics preserved**: response telemetry (`AdapterId`, `Model`) still emits via the synthesized resolution; logs flag the request as `Origin = "url-override"` so operators can distinguish ad-hoc traffic from registered-source traffic.

### Negative

- **Adds a second routing path** to the framework. Mostly cosmetic — the synthesized-resolution branch is small and the downstream code is identical — but reviewers should be aware that "routed via registry" and "routed via override" both reach `request.InternalConnectionString = resolution.Member.ConnectionString` from different upstream paths.
- **Caller can shoot themselves in the foot** by sending requests to URLs that the adapter doesn't know how to handle. The framework can't validate compatibility at compose time. Mitigation: clear error from the adapter when the URL responds in an unexpected shape.
- **Possible misuse**: callers might reach for URL override even when source-based routing would serve them better (e.g., when they have multiple stable endpoints and just want load balancing). The XML doc on `WithUrl` and the ADR linked from it should make the trade-off explicit.

## Alternatives considered

- **Dynamic source registration** via `IAiSourceRegistry.RegisterSource(...)` from the caller side. Already supported, but requires a sync layer between the caller's domain and Koan's registry. The friction the consumer reported is exactly that sync layer; this ADR removes it.
- **A separate `Client.ChatAt(url, ...)` static** rather than a builder method. Rejected — the conversation builder is the idiomatic surface; adding parallel static overloads splits the API.
- **Inferring provider from URL scheme** (e.g., `ollama://...` → ollama adapter). Rejected — schemes aren't reserved for providers; URLs in the wild are bare HTTP. Explicit `provider` parameter is clearer.
- **Lifting `InternalConnectionString` to public**. Rejected — the field's name explicitly signals "set by the router, not the caller"; flipping that semantic mid-stream invites confusion. The new override fields preserve the contract.

## References

- [ADR-0015 / AI-0007 inference servers interop](AI-0007-inference-servers-interop.md) — source/member architecture and `InternalConnectionString` channel.
- [AI-0009 multi-service routing and policies](AI-0009-multi-service-routing-and-policies.md) — registry-driven routing semantics.
- [AI-0010 entrypoint and augmentations](AI-0010-entrypoint-and-augmentations.md) — conversation builder surface.
- [src/Koan.AI.Contracts/Models/AiChatRequest.cs](../../src/Koan.AI.Contracts/Models/AiChatRequest.cs)
- [src/Koan.AI/Pipeline/AiCategoryRouter.cs](../../src/Koan.AI/Pipeline/AiCategoryRouter.cs)
- [src/Koan.AI/AiConversationBuilder.cs](../../src/Koan.AI/AiConversationBuilder.cs)
