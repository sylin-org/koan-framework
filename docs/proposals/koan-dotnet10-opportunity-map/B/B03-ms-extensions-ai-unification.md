# B3 — Unify Koan.AI over Microsoft.Extensions.AI

**Intent**: Wrap Koan.AI around **Microsoft.Extensions.AI** (`IChatClient`, `IEmbeddingGenerator`) while keeping escape hatches to provider SDKs (Ollama, Azure OpenAI, OpenAI). citeturn5search0turn5search1

## Plan

1. Introduce `Koan.AI.Abstractions.MEAI` adapters that implement Koan’s current contracts on top of `IChatClient` and friends.
2. Provide streaming alignment with SSE (A2) and tool/function calling plumbing through ME.AI. citeturn5search2
3. Migration guide for existing Koan AI providers; keep Ollama and others via thin shims.

## Acceptance Criteria

- Demo swaps model backends with zero controller changes.
- Streaming works end-to-end with SSE.
- Telemetry hooks align with `Microsoft.Extensions.*` conventions. citeturn5search0

## Microsoft.Extensions.AI – Capabilities & Ergonomics Snapshot

- **Unified primitives**: `IChatClient`, `ITextGenerator`, `IEmbeddingGenerator`, tool-call workflow, with DI-first patterns (`AddChatClient`, `AddEmbeddingGenerator`).
- **Strong defaults**: automatic HTTP handlers, retry, diagnostics, configuration via named instances and `IConfigureOptions`.
- **Pipeline hooks**: Middleware (`IChatClientMiddleware`) and message stores to customize prompt/response shaping.
- **Provider ecosystem**: First-party adapters for Azure OpenAI, OpenAI, Ollama, Hugging Face, plus generic REST connectors.
- **Streaming & tool calling**: Native streaming events and structured tool invocation, but raw payloads place formatting burden on consumers.

While the surface is capable, the ergonomics are intentionally low-level: developers assemble primitives, juggle abstractions, and wire middleware. The experience is powerful but verbose—each provider requires separate configuration, prompt templates are DIY, and cross-cutting concerns (observability, caching, guardrails) are left to the host to compose.

## Koan.AI Today – Strengths & Gaps

- **DX-first abstractions**: High-level `IAgent`, `PromptRecipe`, and task-specific helpers with opinionated defaults.
- **Adapter consistency**: Koan connectors expose uniform options, provenance, and telemetry out-of-the-box.
- **Streaming alignment**: Tight coupling with Koan’s SSE story (A2) and web-layer ergonomics.
- **Guardrails & provenance**: Boot reports, policy enforcement, and standardized observability.

Koan’s value is clarity and minimal cognitive load, but providers are hand-crafted per SDK. We lack alignment with the emerging Microsoft.Extensions.AI ecosystem, and duplication (custom retry, custom middleware) increases maintenance.

## Design Intent

Unify the developer journey by wrapping Microsoft.Extensions.AI primitives with Koan’s intentional UX. We keep Koan’s façade—the mental model devs already enjoy—but delegate execution to ME.AI for transport, telemetry plumbing, and provider breadth.

### DX Principles

1. **Single mental model**: Developers interact with `KoanAiClient`/`KoanPrompt`/`KoanSkill` not ME.AI types directly.
2. **Convention over composition**: Koan auto-configures ME.AI graphs for common workflows (chat, embeddings, tool calling) with minimal setup.
3. **Observable by default**: All requests registered through Koan’s provenance/telemetry surfaces, piggybacking on ME.AI diagnostics.
4. **Composable escape hatches**: Advanced users can access the underlying `IChatClient` or plug custom middleware without forking Koan.

## Proposed Architecture

```
┌──────────────┐          ┌─────────────────────┐          ┌────────────────────┐
│  Koan APIs   │  uses    │  Koan.AI Integrator │  composes│ Microsoft.Extensions│
│ (IAgent,     ├─────────►│ (facades + default  ├─────────►│ .AI primitives      │
│ Prompt, etc.)│          │  pipelines)          │          │ (IChatClient, etc.) │
└──────────────┘          └─────────────────────┘          └────────────────────┘
```

### Layers & Responsibilities

- **Koan.AI.Abstractions**: Maintains existing high-level interfaces (`IAgent`, `IConversation`, `IAIStream`).
- **Koan.AI.MEAI (new)**: Implements the abstractions using ME.AI clients. Owns:
  - `KoanChatClient` façade (wrapping `IChatClient`).
  - Prompt recipes -> ME.AI `ChatMessage` translator.
  - Streaming bridge -> Koan SSE via `IAsyncEnumerable<ChatResponseMessage>`.
  - Tool mediation (`KoanToolCatalog`) hooking into ME.AI function-calling.
- **Koan.AI.Providers**: Thin packages that register ME.AI providers (Azure OpenAI, OpenAI, Ollama, etc.) with Koan defaults.

## Koan.AI Pillar (ME.AI-powered)

### Pillar Contract

```csharp
[AiEntity]
public sealed class SupportTicket : Entity<SupportTicket>
{
	[AiField(AiFieldKind.Text)]
	public string IssueSummary { get; init; }

	[AiField(AiFieldKind.Text)]
	public string ConversationTranscript { get; init; }

	[AiField(AiFieldKind.Image, Source = AiFieldSource.StorageId)]
	public string? ScreenshotId { get; init; }
}

services.AddKoanAi(ai => ai
	.UseDefaultChatClient("openai:gpt-4o-mini")
	.AddEntityPipeline<SupportTicket>(pipeline => pipeline
		.EnableRetrieval()
		.EnableModeration()
		.EnableVision()));
```

- **Attributes as intent**: `AiEntity` and `AiField` (with `AiFieldKind` enums such as `Text`, `Embedding`, `Image`, `Audio`) map entity members to ME.AI middleware. Optional `Source` hints (`StorageId`, `Binary`, `Url`) let Koan fetch payloads (e.g., resolve storage IDs or load binary blobs).
- **Entity pipelines**: fluent builder wrapping ME.AI middleware stacks—`EnableRetrieval()` wires Koan’s RAG defaults (vector store + retriever), `EnableModeration()` inserts content filters, `EnableVision()` attaches image-to-text or multimodal clients.
- **Defaults**: without configuration, the pipeline chooses sensible components (SQLite conversation store, Redis vector cache, Azure Content Safety) and publishes them via provenance. Hosts can override per-entity or per-field.

### Middleware Composition

- **Retrieval**: `EnableRetrieval()` wires `KoanRagMiddleware` which orchestrates ME.AI’s `IRetrievalMiddleware` with Koan adapters. Koan auto-selects the vector store: `Koan.AI.Weaviate` (existing) or `Koan.AI.RedisVector` (new) implementing ME.AI `IVectorStore`.
- **Moderation**: `EnableModeration()` adds ME.AI content-safety middleware (Azure or OpenAI) but surfaces configuration via Koan options. Moderation results feed back into Koan’s provenance and guardrail policy system.
- **Vision / Speech**: `EnableVision()` or `EnableAudio()` registers ME.AI multimodal generators. Attributes mark which fields supply binary or storage references so Koan can stream them into ME.AI without manual wiring.

### Adapter Story Refresh

- **Weaviate Adapter**: Implement `IVectorStore` + `IVectorSearch` on top of the existing Weaviate client, exposing Koan options (`Koan:AI:Vector:Weaviate`). Koan-specific metadata (tenant, collection) is derived from entity type names and `AiField` decorations.
- **Redis Vector (new)**: Provide `Koan.AI.RedisVector` package using Redis Stack `FT.CREATE`/`HSET` semantics. The adapter implements ME.AI vector contracts and ships with automatic schema provisioning. Koan provenance records the index, dimension, and eviction policy.
- **Other Providers**: Storage (Azure Blob, S3) and caching remain Koan-first; ME.AI is only the execution engine.

### DX Narrative

1. Developer decorates entities with intuitive attributes (`AiField(AiFieldKind.Embedding, Model = "text-embedding-3-large")`).
2. Registers the entity pipeline with `AddEntityPipeline<T>()`, optionally enabling RAG, moderation, or multimodal support.
3. Consumes high-level helpers via expressive extensions:

   ```csharp
   // Conversational support (chat)
   await SupportTicket.Interact(ticketId, prompt => prompt
      .UseRecipe("triage")
      .UseRagContext(rag => rag.WithMaxDocuments(5))
      .Moderate());

   // Raw chat shortcut
   await SupportTicket.Chat(ticketId, "Summarise the issue for Tier 2");

   // Targeted embeddings
   await SupportTicket.Embed(ticketId, fields => fields
      .Include(x => x.IssueSummary)
      .Include(x => x.ConversationTranscript));

   // Vision / multimodal
   var analysis = await SupportTicket.Understand(ticketId, x => x.ScreenshotId);
   ```

   These helpers are thin wrappers over `Interact` so the mental model stays compact: `Chat` and `Embed` call into the interaction pipeline with preconfigured strategies; `Understand` invokes multimodal middleware. Behind the scenes, Koan resolves the entity, marshals fields into ME.AI messages, runs retrieval via the configured vector store, applies moderation, and streams responses via Koan SSE.

4. Persistence stays intuitive:

   ```csharp
   [AiField(AiFieldKind.Embedding, Mode = AiEmbeddingMode.Auto)]
   public Vector512 SummaryEmbedding { get; init; }

   await ticket.Save(); // On Save(), Koan detects Auto embeddings,
   					// calls embedding pipeline, and persists both
   					// the entity (SQL) and vector record (Weaviate/Redis).
   ```

   The `AiEmbeddingMode.Auto` flag asks Koan to materialize vectors before the entity commit completes. Hooks plug into the Koan.Data pipeline (pre-commit interceptors) to ensure vector stores stay in sync. Developers can opt-out or force manual control via `AiEmbeddingMode.Manual`.

5. Provenance captures the full pipeline: provider, vector store, moderation policy, prompts used. Operators see a cohesive view without knowing ME.AI internals.

### Bridging Advanced Scenarios

- Developers can reach into `IChatClient` or `IVectorStore` through optional injection points (`IKoanAiContext`) when bespoke customization is needed.
- All pipeline components remain DI-friendly: add/remove middleware, override defaults per tenant, or inject custom tools.

## Key Components

### 1. Koan AI Builder DSL

```csharp
services.AddKoanAi(ai => ai
		.UseDefaultChatClient("openai:gpt-4o-mini")
		.EnableStreaming()
		.AttachTool<WeatherSkill>()
		.WithPromptLibrary(options => options.LoadFromAssembly<SupportPrompts>()));
```

- Binds to ME.AI’s named client builder under the hood, but presents Koan’s fluent DSL.
- Automatically registers provenance: `Koan:AI:Chat:Primary` entry renders provider/version/region.

### 2. Prompt & Recipe Engine

- Koan prompt descriptors compile into ME.AI `PromptTemplate` objects.
- Shared schema for parameters and safety policies (`PromptPolicyAttribute`).
- Built-in ME.AI middleware ensures Koan guardrails (PII scrubbing, rate limits) fire before provider invocation.

### 3. Streaming & SSE Alignment

- `KoanChatStream` implements Koan streaming contract, internally connected to `IAsyncEnumerable<StreamingChatCompletionUpdate>`.
- SSE controllers reuse the WebSocket/SSE infrastructure (A2) while relying on ME.AI streaming semantics.

### 4. Tool/Adapter Coordination

- `KoanToolRegistry` collects Koan adapters (domain-specific skills) and exposes them to ME.AI tool-calling.
- Adapters advertise capabilities (embedding support, multi-turn memory) using metadata consumed by Koan’s orchestrators.

### 5. Observability & Telemetry

- Wrap ME.AI diagnostics with Koan’s provenance settings (`provider`, `model`, `throughput`, `latency`).
- Emit structured events to Koan logging pipelines (structured logging + OpenTelemetry).

## Provider & Adapter Story

- **Thin provider packages** (e.g., `Koan.AI.AzureOpenAI`) reference the ME.AI provider, expose Koan options, and contribute to provenance.
- **Migration path**: existing Koan adapters (Ollama, Azure OpenAI) reimplemented as ME.AI configuration modules while preserving the old façade signature.
- **Extensibility**: third parties can plug their ME.AI-compatible adapters by implementing `IKoanAiProviderContributor`.

## UX Improvements

- **One-line adoption**: `services.AddKoanAi()` bootstraps default chat + embeddings using config section `Koan:AI`.
- **Zero-copy streaming**: host developers consume `IAgentStream` without touching ME.AI types.
- **Recipe catalogs**: `AddPromptCatalog<TCatalog>()` loads annotated prompts; Koan handles ME.AI prompt template instantiation.
- **Guided defaults**: Koan pre-configures temperature/top-p with environment-specific defaults (prod vs dev) and surfaces config hints via provenance.

## Migration Considerations

- Maintain backward-compatible Koan APIs with ME.AI-backed implementations; mark legacy provider packages as thin wrappers forwarding to new architecture.
- Provide a codemod guide: update DI registration (`AddKoanAiLegacy()` -> `AddKoanAi()`), optional prompt annotation adjustments.
- Document escape hatches: how to obtain the inner `IChatClient` for edge cases.

## Roadmap

1. **Phase 1**: Build `Koan.AI.MEAI` core, wrap chat & embeddings, deliver default provider packages (Azure OpenAI, OpenAI, Ollama).
2. **Phase 2**: Tooling integration—Koan tool registry + ME.AI function calling, SSE streaming alignment.
3. **Phase 3**: Observability upgrades (provenance, metrics), guardrail middleware, sample applications.
4. **Phase 4**: Deprecate legacy adapters, finalize migration docs, expand prompt catalog features.

## Success Criteria Extensions

- Unified builder config: single `appsettings` section drives multiple providers via ME.AI.
- Streaming + tool calling demos run without referencing ME.AI types in user code.
- Koan provenance dashboard lists ME.AI providers with correct model metadata and diagnostics.
