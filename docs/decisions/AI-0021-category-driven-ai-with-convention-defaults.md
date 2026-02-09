---
id: AI-0021
slug: AI-0021-category-driven-ai-with-convention-defaults
domain: AI
status: Proposed
date: 2026-02-08
---

# ADR: Category-Driven AI with Convention-Inferred Defaults

**Contract**

- **Inputs:** AI category configuration from `Koan:Ai:{Category}:*` (Chat, Embed, Ocr), entity types with optional `[Embedding]`/`[AiContext]`/`[Ocr]` attributes, raw inputs (strings, byte arrays) for on-demand operations, source/member infrastructure from `Koan:Ai:Sources:*`.
- **Outputs:** Category-routed AI results (`string` for Chat/Ocr, `float[]` for Embed) via static `Client` facade, convention-inferred metadata for undecorated entities, per-category boot report entries showing resolved source/model/health, split adapter interfaces (`IChatAdapter`, `IEmbedAdapter`, `IOcrAdapter`) replacing monolithic `IAiAdapter`.
- **Error Modes:** On-demand operation on undecorated entity succeeds via convention inference; on-demand operation returning empty results logs diagnostic guidance (not exception); missing category source falls back to global default source; task category with no dedicated adapter delegates through `Via` protocol category; no adapter available for any protocol yields bootstrap warning.
- **Acceptance Criteria:** `Client.Chat("message")`, `Client.Embed("text")`, and `Client.Ocr(imageBytes)` work with zero configuration against auto-discovered Ollama; `Client.Embed(entity)` succeeds on any entity without `[Embedding]` using AllStrings convention; categories route independently (Embed to OpenAI while Chat to Ollama); `[Embedding]` attribute gates only lifecycle integration (auto-embed on save), never on-demand operations; boot report shows per-category routing at a glance.

**Edge Cases**

- Entity with no string properties passed to `Client.Embed(entity)`: Convention falls back to JSON serialization of all public readable properties; if entity is empty, operation fails with clear message ("No embeddable content found on {Type}").
- `SemanticSearch<T>()` on type with no stored vectors: Returns empty list, logs dev-mode guidance ("No vectors found for {Type}. Add [Embedding] to auto-embed on save, or store vectors manually via Vector<T>.Save()").
- `Client.Ocr(entity)` on entity with multiple `byte[]` properties and no `[Ocr]` attribute: Convention prefers properties named Image, Photo, Scan, Document (case-insensitive); if single `byte[]` property exists, uses it; if ambiguous, fails with clear message listing candidates.
- OCR category with no dedicated `IOcrAdapter` registered: Delegates through `Via: Chat` using the OCR category's configured model (glm-ocr); adapter receives image via multimodal message parts.
- `Client.Scope(embed: "openai-prod")` with `Client.Embed(entity)` inside scope: Scope override wins over category default; convention-inferred metadata still applies for text generation.
- Entity with `[Embedding]` saved via `entity.Save()` when AI provider is unavailable: Entity persists normally; embedding marked as failed in `EmbeddingState<T>`; background worker retries per existing AI-0020 behavior.
- Category configured with source that has no healthy members: Circuit breaker fires; if fallback source configured, routes there; otherwise fails with source health diagnostic.

## Context

The Koan.AI pillar currently exposes a unified `IAiAdapter` interface that bundles chat, streaming, and embedding operations into a single contract. All operations route through the same source/member pool via `AiRoutingEngine`, which already internally distinguishes between `ResolveChat` and `ResolveEmbeddings` (passing capability names like `"Chat"` and `"Embedding"`). `AiSourceDefinition.Capabilities` keys on these same strings. The infrastructure for per-operation routing partially exists, but is not surfaced to the developer experience.

This creates three problems:

1. **Routing is provider-centric, not operation-centric.** Developers who want embeddings from OpenAI and chat from Ollama must define two sources with different capability metadata and hope the router picks correctly. The intent is indirect.

2. **`[Embedding]` gates on-demand operations.** `SemanticSearch<T>()` throws `InvalidOperationException` if the entity lacks `[Embedding]`. Calling `Client.Embed(entity)` requires the attribute. This conflicts with Koan's "sane defaults" philosophy — the framework should work first and let attributes customize.

3. **`IAiAdapter` violates ISP.** An embedding-only provider (Cohere Embed, Voyage AI) must stub `ChatAsync` and `StreamAsync`. A chat-only provider stubs `EmbedAsync`. The monolithic interface forces providers to lie about their capabilities.

Additionally, the framework lacks an OCR category despite OCR being a common AI operation with dedicated provider ecosystems (Azure Document Intelligence, Google Cloud Vision) and specialized models (GLM-OCR). Developers currently route OCR through `Client.Understand(imageBytes, prompt)` which is semantically vague and requires manual prompt engineering.

## Decision

### Part 1: Two-Tier Category Architecture

Introduce **AI categories** as the primary routing concept, replacing the current flat source/capability model. Categories come in two tiers:

**Protocol categories** represent fundamentally different AI wire protocols with distinct input/output types and adapter interfaces:

| Category | Input | Output | Adapter Interface | Provider API |
|----------|-------|--------|-------------------|-------------|
| Chat | text + messages | text (streaming) | `IChatAdapter` | `/api/generate`, `/v1/chat/completions` |
| Embed | text batch | `float[][]` | `IEmbedAdapter` | `/api/embeddings`, `/v1/embeddings` |

**Task categories** represent specialized AI operations that delegate through a protocol category by default, but can be backed by a dedicated adapter when one exists:

| Category | Input | Output | Adapter Interface | Default Via |
|----------|-------|--------|-------------------|------------|
| Ocr | image bytes | text | `IOcrAdapter` (optional) | Chat |

Task categories get full top-level DX parity with protocol categories. The `Via` delegation is an internal routing concern, invisible to the developer.

**Taxonomy test for new categories** — a candidate must pass all four:

1. Distinct input type from existing categories
2. Dedicated provider ecosystem exists (not just a prompt variation)
3. Default model differs from Chat default
4. One-line DX is meaningfully clearer than the generic alternative

### Part 2: Static Facade — The Escalation Ladder

The `Client` class provides a smooth gradient from simple to powerful with no API cliffs:

```csharp
// ── Tier 1: One-liner (zero config, sane defaults) ──────────────

var answer = await Client.Chat("Explain monads");
var vector = await Client.Embed("Article about monads...");
var text   = await Client.Ocr(screenshotBytes);

// ── Tier 2: Options (single-call control) ────────────────────────

var answer = await Client.Chat("Explain monads", new ChatOptions
{
    Model = "llama3.2:70b",
    Temperature = 0.3,
    SystemPrompt = "You are a Haskell tutor"
});

var vector = await Client.Embed("Article about monads...", new EmbedOptions
{
    Model = "text-embedding-3-large"
});

var text = await Client.Ocr(screenshotBytes, new OcrOptions
{
    Format = OcrFormat.Markdown,
    Language = "ja"
});

// ── Tier 3: Streaming (Chat) ─────────────────────────────────────

await foreach (var chunk in Client.Stream("Explain monads"))
{
    Console.Write(chunk);
}

// ── Tier 4: Entity convention (no attribute required) ────────────

var vector = await Client.Embed(article);           // AllStrings convention
var answer = await Client.Chat("Summarize", article); // Key:Value serialization

// ── Tier 5: Scoped routing (multi-call, per-category) ────────────

using (Client.Scope(chat: "ollama-gpu", embed: "openai-prod"))
{
    var answer = await Client.Chat("Analyze this");   // → ollama-gpu
    var vector = await Client.Embed("some text");     // → openai-prod
}

// ── Tier 6: Rich results (when metadata matters) ─────────────────

ChatResult result = await Client.ChatResult("Explain monads");
// result.Text, result.Model, result.TokensUsed, result.Latency, result.AdapterId

EmbedResult result = await Client.EmbedResult("some text");
// result.Vector, result.Model, result.Dimension, result.TokensUsed

OcrResult result = await Client.OcrResult(screenshotBytes);
// result.Text, result.Format, result.Confidence, result.Model
```

The `Engine` facade is deprecated. `Client` is the single entry point at every tier.

### Part 3: Convention-Inferred Defaults

Every category operation succeeds on any entity without decoration. Attributes customize; they never gate.

**Convention resolution chains:**

Embed:
1. `[Embedding]` template or policy → use configured text composition
2. No attribute → scan public string properties, concatenate with `"\n"`
3. No string properties → JSON serialization of all public readable properties
4. Empty result → fail with clear message

Chat entity context:
1. `[AiContext]` template → use configured serialization
2. No attribute → serialize public readable properties as `"Key: Value\n"` pairs
3. Skip navigation properties, `byte[]`, collections

OCR entity:
1. `[Ocr]` Property → use explicit target
2. No attribute → scan for `byte[]` properties
3. Prefer convention names: Image, Photo, Scan, Document (case-insensitive)
4. Single `byte[]` property → use it
5. Ambiguous → fail with clear message listing candidates

**The attribute boundary:**

| Concern | Convention (no attribute) | Attribute |
|---------|--------------------------|-----------|
| On-demand `Client.Embed(entity)` | AllStrings | Template/policy from attribute |
| On-demand `Client.Chat("msg", entity)` | Key:Value pairs | `[AiContext]` template |
| On-demand `Client.Ocr(entity)` | Finds `byte[]` by convention | `[Ocr]` explicit property |
| `SemanticSearch<T>()` query embedding | Convention (works) | Convention or attribute (works) |
| `entity.Save()` → auto-embed | **No** | **Yes** — attribute = lifecycle opt-in |
| Change detection (SHA256) | No | Yes |
| Async background processing | No | Yes (`Async = true`) |
| `EmbeddingState<T>` tracking | No | Yes |

The attribute's single gating responsibility is **lifecycle integration**. On-demand operations are convention territory.

**`EmbeddingMetadata` becomes convention-first:**

```csharp
// Never returns null — convention inferred when attribute absent
public static EmbeddingMetadata Resolve<TEntity>()
{
    var attr = typeof(TEntity).GetCustomAttribute<EmbeddingAttribute>();
    if (attr != null) return BuildFrom(attr);

    return InferConvention<TEntity>();
}

private static EmbeddingMetadata InferConvention<TEntity>()
{
    var stringProps = typeof(TEntity)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(string) && p.CanRead)
        .Where(p => !p.GetCustomAttributes<EmbeddingIgnoreAttribute>().Any())
        .ToList();

    return new EmbeddingMetadata
    {
        Policy = EmbeddingPolicy.AllStrings,
        Properties = stringProps,
        LifecycleEnabled = false,  // convention = no auto-embed on save
    };
}
```

**Diagnostic guidance for empty results:**

When `SemanticSearch<T>()` returns empty because no vectors are stored, the framework logs guidance in development mode:

```
⚠ SemanticSearch<Article> returned 0 results — no stored vectors found.
  To auto-embed on save:  Add [Embedding] to Article
  To store manually:      await Vector<Article>.Save(id, await Client.Embed(article));
```

This appears as a log message, not an exception. The operation succeeded correctly (zero matches).

### Part 4: Adapter Interface Split (ISP)

Replace monolithic `IAiAdapter` with category-specific interfaces:

```csharp
// Core identity — all adapters
public interface IAiAdapter
{
    string Id { get; }
    string Name { get; }
    string Type { get; }
    Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default);
}

// Protocol adapters — implement what you support
public interface IChatAdapter : IAiAdapter
{
    bool CanServe(AiChatRequest request);
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, CancellationToken ct = default);
}

public interface IEmbedAdapter : IAiAdapter
{
    Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default);
}

// Task adapters — optional, for dedicated providers
public interface IOcrAdapter : IAiAdapter
{
    Task<OcrResponse> RecognizeAsync(OcrRequest request, CancellationToken ct = default);
}
```

Existing adapters implement what they support:

```csharp
// Ollama supports chat and embed — not OCR directly
internal sealed class OllamaAdapter : IChatAdapter, IEmbedAdapter { ... }

// LM Studio supports chat and embed
internal sealed class LMStudioAdapter : IChatAdapter, IEmbedAdapter { ... }

// Hypothetical dedicated OCR provider
internal sealed class AzureDocIntelAdapter : IOcrAdapter { ... }
```

The legacy `IAiAdapter` with all methods remains as a convenience base during migration but is marked `[Obsolete]`.

### Part 5: Category-Aware Routing

The `AiRoutingEngine` gains a category resolver:

```csharp
internal sealed record AiCategoryRoute
{
    public required string Name { get; init; }           // "Chat", "Embed", "Ocr"
    public required Type AdapterInterface { get; init; } // typeof(IChatAdapter)
    public string? Via { get; init; }                    // null = protocol, "Chat" = delegates
    public string? DefaultSource { get; init; }          // from Koan:Ai:{Category}:Source
    public string? DefaultModel { get; init; }           // from Koan:Ai:{Category}:Model
}
```

Resolution flow for task categories with `Via` delegation:

```
Client.Ocr(imageBytes)
  → CategoryRouter.Resolve("Ocr")
  → Check: IOcrAdapter registered?
    → Yes: route to dedicated adapter (e.g., Azure Document Intelligence)
    → No:  resolve Via category ("Chat")
           → route to IChatAdapter with OCR model and image payload
           → adapter receives: model=glm-ocr, messages=[{role:user, parts:[image, prompt]}]
```

### Part 6: Scoped Routing

`Client.Scope()` replaces `Client.Context()` with per-category targeting:

```csharp
// Per-category overrides
using (Client.Scope(chat: "ollama-gpu", embed: "openai-prod"))
{
    await Client.Chat("message");  // → ollama-gpu
    await Client.Embed("text");    // → openai-prod
    await Client.Ocr(image);       // → default OCR source (unaffected)
}

// Global override (all categories)
using (Client.Scope(all: "ollama-local"))
{
    // Everything routes to ollama-local
}
```

Internally backed by `AsyncLocal<ImmutableStack<AiCategoryScope>>` following the existing `AiContextScope` pattern, extended with per-category slots.

### Part 7: Per-Category Options Types

Each category gets a focused options record:

```csharp
public record ChatOptions
{
    public string? Model { get; init; }
    public string? SystemPrompt { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public double? TopP { get; init; }
    public string? Source { get; init; }
    public string? ResponseFormat { get; init; }
    public byte[]? Image { get; init; }           // multimodal (absorbs Understand)
    public string? ImageMimeType { get; init; }
}

public record EmbedOptions
{
    public string? Model { get; init; }
    public string? Source { get; init; }
}

public record OcrOptions
{
    public OcrFormat Format { get; init; }         // PlainText, Markdown, Structured
    public string? Language { get; init; }
    public string? MimeType { get; init; }
    public string? Model { get; init; }
    public string? Source { get; init; }
}

public enum OcrFormat { PlainText, Markdown, Structured }
```

`ChatOptions` absorbs what `Client.Understand()` currently does via the `Image` property. The `Understand` method is deprecated.

### Part 8: Configuration

```json
{
  "Koan": {
    "Ai": {
      "Chat": {
        "Source": "ollama-local",
        "Model": "llama3.2"
      },
      "Embed": {
        "Source": "openai-embeddings",
        "Model": "text-embedding-3-large"
      },
      "Ocr": {
        "Source": "ollama-local",
        "Model": "glm-ocr",
        "Via": "Chat"
      },
      "Sources": {
        "ollama-local": {
          "Provider": "ollama",
          "Members": [
            { "Name": "ollama-local::host", "ConnectionString": "http://localhost:11434" }
          ]
        },
        "openai-embeddings": {
          "Provider": "openai",
          "Members": [
            { "Name": "openai-embeddings::primary", "ConnectionString": "https://api.openai.com/v1" }
          ]
        }
      }
    }
  }
}
```

**Zero-config default:** When no category configuration is present, all categories resolve to the auto-discovered Ollama source. Chat uses Ollama's default chat model, Embed uses Ollama's default embedding model, OCR uses `glm-ocr`. The `Via` default for OCR is `"Chat"`.

### Part 9: Boot Report

```
╔══ Module: Koan.AI ═══════════════════════════════════════╗
║                                                           ║
║ Categories                                                ║
║   Chat     → ollama-local (llama3.2)                     ║
║   Embed    → openai-embeddings (text-embedding-3-large)  ║
║   Ocr      → ollama-local (glm-ocr) via Chat            ║
║                                                           ║
║ Sources                                                   ║
║   ollama-local        1 member  healthy  (auto-discovery)║
║   openai-embeddings   1 member  healthy  (explicit)      ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝
```

### Part 10: Entity Attribute Simplification

With category-level defaults, `[Embedding]` sheds its routing concerns:

```csharp
// Before (AI-0020): routing escape hatches on data attribute
[Embedding(
    Policy = EmbeddingPolicy.AllStrings,
    Source = "openai-embeddings",           // routing concern
    Model = "text-embedding-3-large",       // routing concern
    MaxTokens = 8191
)]
public class Article : Entity<Article> { ... }

// After: data concerns only — routing is the Embed category's job
[Embedding(
    Policy = EmbeddingPolicy.AllStrings,
    MaxTokens = 8191
)]
public class Article : Entity<Article> { ... }

// Bare attribute: lifecycle opt-in with full convention defaults
[Embedding]
public class Article : Entity<Article> { ... }

// No attribute: on-demand operations still work via convention
public class Article : Entity<Article> { ... }
```

`Source` and `Model` remain on `[Embedding]` for per-entity overrides but shift from common usage to rare escape hatch.

## Consequences

- **Positive:**
  - Every on-demand AI operation works on any entity with zero decoration, honoring Koan's sane-defaults philosophy.
  - Categories provide independent routing per operation type — the most common production pattern (different providers for chat vs embeddings) becomes a first-class configuration concept.
  - ISP-compliant adapter interfaces eliminate capability stubbing; providers implement only what they support.
  - Task categories (Ocr) get full DX parity without requiring dedicated adapter implementations — `Via` delegation handles the common case.
  - The escalation ladder (one-liner → options → streaming → entity convention → scoped routing → rich results) provides a smooth gradient with no API cliffs.
  - `[Embedding]` attribute simplifies to a lifecycle opt-in; `Source`/`Model` become rare overrides instead of required routing configuration.
  - Boot report shows per-category routing at a glance, eliminating the mental mapping from source capabilities to routing outcomes.
  - Taxonomy test prevents category proliferation — operations that are just prompt variations of Chat don't qualify.
  - Convention-inferred metadata makes `SemanticSearch<T>()` and `Client.Embed(entity)` work immediately during prototyping, before the developer has committed to attribute configuration.

- **Negative / Trade-offs:**
  - Breaking change: `IAiAdapter` splits into `IChatAdapter` + `IEmbedAdapter`. Existing adapter implementations must be updated. Mitigated by keeping legacy `IAiAdapter` as `[Obsolete]` during transition.
  - `Client.Understand()` deprecated in favor of `Client.Chat(message, new ChatOptions { Image = ... })`. Existing call sites require migration.
  - `Client.Context()` deprecated in favor of `Client.Scope()` with per-category parameters. Existing scoped routing code requires migration.
  - `Engine` facade deprecated. Call sites using `Engine.Chat()` must migrate to `Client.Chat()`.
  - Convention inference adds a small reflection cost on first use per entity type. Mitigated by caching in `EmbeddingMetadata` (same pattern as current attribute-based caching).
  - `Via` delegation adds an indirection layer for task categories that may complicate debugging. Mitigated by boot report clearly showing delegation path.

## Implementation Notes

1. **Phase 1: Adapter interface split.** Extract `IChatAdapter` and `IEmbedAdapter` from `IAiAdapter`. Update `OllamaAdapter` and `LMStudioAdapter` to implement both. Mark `IAiAdapter` as `[Obsolete]`. Update `AiRoutingEngine` to resolve by interface type.

2. **Phase 2: Category routing.** Introduce `AiCategoryRoute` and `AiCategoryRouter`. Bind categories to configuration sections (`Koan:Ai:Chat`, `Koan:Ai:Embed`, `Koan:Ai:Ocr`). Implement `Via` delegation for task categories. Update `AiSourceRegistry` to support category-scoped source resolution.

3. **Phase 3: Convention-inferred defaults.** Refactor `EmbeddingMetadata.Get<T>()` to `EmbeddingMetadata.Resolve<T>()` with convention fallback. Update `SemanticSearch<T>()` to use `Resolve` instead of `Get`. Add diagnostic guidance logging for empty vector results. Implement `AiContextMetadata` convention chain for Chat entity context. Implement `OcrMetadata` convention chain for entity OCR.

4. **Phase 4: Client facade.** Add `Client.Embed(entity)` overload with convention support. Add `Client.Chat(message, entity)` overload with context serialization. Add `Client.Ocr(byte[])` and `Client.Ocr(entity)`. Add `Client.Scope()` with per-category parameters. Add `Client.ChatResult()`, `Client.EmbedResult()`, `Client.OcrResult()` rich-result variants. Deprecate `Client.Understand()`, `Client.Context()`, and `Engine.*`.

5. **Phase 5: OCR category.** Define `IOcrAdapter`, `OcrRequest`, `OcrResponse`, `OcrOptions`, `OcrResult`. Implement `Via: Chat` delegation (multimodal message with image parts and OCR prompt). Register `glm-ocr` as default OCR model. Add OCR convention resolver for entity `byte[]` properties.

6. **Phase 6: Boot report and diagnostics.** Update `KoanAutoRegistrar.Describe()` to emit per-category routing entries. Add diagnostic guidance messages for convention-inferred empty results. Update admin surfaces.

## Migration Notes

- `IAiAdapter` implementations must adopt `IChatAdapter` and/or `IEmbedAdapter`. The legacy interface remains functional but deprecated. Adapter authors implement only the interfaces matching their provider capabilities.
- `Client.Understand(imageBytes, prompt)` call sites migrate to `Client.Chat(prompt, new ChatOptions { Image = imageBytes })`.
- `Client.Context(source, provider, model)` call sites migrate to `Client.Scope(all: source)` or per-category `Client.Scope(chat: source)`.
- `Engine.Chat()` / `Engine.Embed()` call sites migrate to `Client.Chat()` / `Client.Embed()`.
- `[Embedding(Source = "...", Model = "...")]` remains functional but becomes unnecessary when category-level configuration provides the same routing. Developers can remove `Source`/`Model` from attributes after configuring `Koan:Ai:Embed` in appsettings.
- `EmbeddingMetadata.Get<T>()` call sites should migrate to `EmbeddingMetadata.Resolve<T>()`. The `Get` method is marked `[Obsolete]` and delegates to `Resolve` internally.
- Existing tests that assert `InvalidOperationException` on missing `[Embedding]` attribute must be updated — the framework no longer throws for on-demand operations on undecorated entities.

## References

- AI-0015 Canonical source-member architecture (source/member routing model)
- AI-0019 Koan.AI zero-config integration on ME.AI (pipeline and registrar patterns)
- AI-0020 Entity-first AI and transaction coordination (lifecycle integration, `[Embedding]` attribute)
- AI-0008 Adapters and registry (adapter identity, capabilities, operations)
- AI-0009 Multi-service routing and policies (fallback groups, health monitoring)
- `src/Koan.AI/Client.cs` (current static facade)
- `src/Koan.AI/Engine.cs` (deprecated by this ADR)
- `src/Koan.AI/Context/AiContextScope.cs` (superseded by `Client.Scope()`)
- `src/Koan.AI/Pipeline/AiRoutingEngine.cs` (extended with category routing)
- `src/Koan.AI.Contracts/Adapters/IAiAdapter.cs` (split by this ADR)
- `src/Koan.AI.Contracts/Sources/AiSourceDefinition.cs` (Capabilities dict already keys on category names)
- `src/Koan.Data.AI/EmbeddingMetadata.cs` (refactored to convention-first)
- `src/Koan.Data.AI/EntityEmbeddingExtensions.cs` (SemanticSearch updated to use Resolve)
