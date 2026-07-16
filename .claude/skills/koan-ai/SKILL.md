---
name: koan-ai
description: Entity-aware AI over Entity<T> — EntityAi.Embed/Chat/Ocr convention inference, [Embedding] auto-embed-on-save, [MediaAnalysis] vision/OCR, the static Koan.AI.Client chat/embed facade, and the conversation builder
pillar: ai
card: docs/reference/cards/ai-data.md
status: current
last_validated: 2026-07-16
---

# Koan AI

## Trigger this skill when you see

- `EntityAi.Embed(entity)` / `.Chat(msg, entity)` / `.Ocr(entity)` — running AI over an `Entity<T>`
- `[Embedding]` / `[Embedding(Template = "...")]` / `[EmbeddingIgnore]` / `[MediaAnalysis(...)]` attributes
- The static `Koan.AI.Client` facade — `Client.Chat(...)`, `Client.Stream(...)`, `Client.Embed(...)`, `Client.Conversation()`
- `IAiPipeline.Prompt(...)` injected for testability; `AiChatRequest` / `AiChatResponse`
- "chat endpoint", "embeddings", "semantic search", "RAG", "OCR", "vision describe", "AI-enriched entity"
- References to `Koan.AI` / `Koan.Data.AI` / `Koan.AI.Connector.Ollama` / `Koan.AI.Connector.LMStudio`
- Per-category routing (`Koan:Ai:Chat/Embed/Ocr`), recipes, `Client.Scope(...)`

## Core principle

**The entity is the prompt.** `EntityAi` (in `Koan.Data.AI`) runs AI verbs directly over your `Entity<T>` — `Embed` / `Chat` / `Ocr` — inferring their content by convention (`[Embedding]` chain → `AllStrings` → JSON). Annotate with `[Embedding]` to opt into auto-embed-on-save; the on-demand verbs work with or without it. The provider is chosen by **package reference** — add `Koan.AI.Connector.Ollama` (default) or `Koan.AI.Connector.LMStudio` and that adapter becomes the runtime (Reference = Intent); there is no client to wire and no provider-specific code. Entity verbs stay **Save / Remove / Query**; the AI verbs are `Embed` / `Chat` / `Ocr`.

<!-- validate -->
```csharp
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;                       // static Client facade
using Koan.Data.AI;                  // EntityAi
using Koan.Data.AI.Attributes;       // [Embedding]
using Koan.Data.Core.Model;          // Entity<T>

[Embedding(Template = "{Title}\n\n{Content}")]      // opt into auto-embed-on-save; pick content by template
public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

public static class ArticleAi
{
    // Entity-aware verbs: the entity *is* the content (convention inference).
    public static async Task<string> Summarize(Article article, CancellationToken ct = default)
    {
        await article.Save();                                  // embedding generated on save
        float[] vector = await EntityAi.Embed(article, ct);    // on-demand embedding
        string answer  = await EntityAi.Chat("Summarize this", article, ct); // entity as context
        return $"{answer} (dims={vector.Length})";
    }

    // Lower-level escape hatch: the static Client speaks raw strings.
    public static async Task<string> AskFreeform(string question, CancellationToken ct = default)
    {
        string reply  = await Client.Chat(question, ct);       // Task<string> — .Text already unwrapped
        float[] embed = await Client.Embed(question, ct);      // raw-text embedding, float[]
        return $"{reply} ({embed.Length}d)";
    }
}
```

## Reference = Intent activation table

| Add this reference | Effect |
|---|---|
| `Koan.AI` + `Koan.AI.Connector.Ollama` | Default inference backend; `Client.Chat/Embed/Stream` + `IAiPipeline` go live. |
| `Koan.AI.Connector.LMStudio` | LM Studio as the runtime — same `Client` surface, different adapter. |
| `Koan.AI.Connector.HuggingFace` / `Koan.AI.Connector.ZenGarden` | Additional inference backends (no OpenAI/Azure connector ships). |
| `Koan.Data.AI` | Entity-aware layer: `EntityAi.Embed/Chat/Ocr`, `[Embedding]`, `[MediaAnalysis]`. |
| a vector connector (Weaviate / Qdrant / Milvus) | Persists `[Embedding]` vectors for KNN search — see `koan-vector`. |

The runtime is whichever connector you referenced; no `Type: OpenAI` config, no client construction. Config root is **`Koan:Ai`** with per-category routing (`Chat` / `Embed` / `Ocr` each carry `Source` + `Model`).

## Attributes you'll declare (Koan.Data.AI.Attributes)

| Attribute | What it does |
|---|---|
| `[Embedding]` · `[Embedding(Template = "{A}\n{B}")]` | Opt the entity into embedding; choose content by `Template` / `Properties` / `Policy` (default `AllStrings`), with optional `Async`, `Model`, `Source`. Class-level — there is **no per-property `[VectorField]`**. |
| `[EmbeddingIgnore]` | Exclude a property from convention-based embedding content. |
| `[MediaAnalysis(Analysis = MediaAnalysis.Describe \| MediaAnalysis.Ocr)]` | Auto-run vision / OCR / transcription on a `MediaEntity` upload; results feed `[Embedding]` text via `MediaAnalysisEmbeddingBridge`. Convention-detected sinks: `AiDescription`, `OcrText`, `Transcript`, `Category`. |

`[Embedding]` stores the generated vector in the elected vector provider; it does not populate a
`float[]` property on the Entity. `Async = true` persists only queue identity/signature/context, then
reloads the current Entity and performs a vector-only write. Worker batch/rate/retry policy is one
host configuration under `Koan:Data:AI:EmbeddingWorker`, not per-type attribute knobs.

## The Client facade (Koan.AI)

`Client` is **static** — no DI, no injection. It resolves the configured pipeline and unwraps results:

- `Client.Chat(string, ct)` → `Task<string>` (`.Text` already extracted) · `Client.Chat<T>(...)` for JSON-schema-typed results
- `Client.Stream(string, ct)` → `IAsyncEnumerable<string>` (token deltas)
- `Client.Embed(string, ct)` → `Task<float[]>` · `Client.EmbedBatch(string[], ct)` → `float[][]`
- `Client.Ocr(byte[], ct)` / `Client.Describe(byte[], ct)` / `Client.Classify` / `Client.Extract<T>` / `Client.Translate` / `Client.Moderate`
- `Client.Conversation().WithSystem(...).WithUser(...).Send(ct)` → `AiChatResponse`; `.Ask(message, ct)` appends a user turn then sends
- `Client.Scope(chat: "fast-local", embed: "cloud")` — `IDisposable` per-category routing override;
  `Client.Embed(text, new EmbedOptions { Source = "cloud" })` is the call-scoped equivalent

When you need a testable seam, inject `IAiPipeline` and call `Prompt(AiChatRequest, ct)` → `AiChatResponse`. There is **no `IAi` interface**, no `ChatAsync` / `StreamAsync` / `TokenizeAsync`, and `AiChatResponse` exposes **`.Text` only** (no `.Content` / `.Choices` / `.Usage` — the rich metadata lives on `ChatResult` via `Client.ChatResult(...)`).

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `_ai.ChatAsync(...)` / injecting an `IAi` interface | Static `Client.Chat(message, ct)` (returns `Task<string>`), or inject `IAiPipeline` and call `Prompt(...)`. |
| `response.Content` / `response.Choices` / `response.Usage` | `Client.Chat(...)` already returns the text; `AiChatResponse.Text` is the only field. For tokens/latency use `Client.ChatResult(...)`. |
| `Client.Converse().PromptAsync(ct)` | `Client.Conversation().WithSystem(...).WithUser(...).Send(ct)` (or `.Ask(message, ct)`). |
| `Vector<T>.SearchAsync(query)` over a raw string | `Client.Embed(query)` → `Vector<T>.Search(float[], ...)` — Search takes a precomputed embedding. See `koan-vector`. |
| `[VectorField]` on a `float[]` property | Class-level `[Embedding]` — there is no per-property vector attribute. |
| `Koan:AI:Providers` / `Type: "OpenAI"` config | Config root is `Koan:Ai`; the provider is the referenced connector (Ollama / LMStudio shipped). No OpenAI connector. |
| `IAi.TokenizeAsync(...)` | No such method exists on any type — remove it. |
| Hand-built RAG agent loop in Koan | The agentic / RAG surface is **migrating to Agyo** (ARCH-0089) — see below. |

## Escape hatches

- **Raw strings, no entity**: drop to `Client.Chat(...)` / `Client.Embed(...)` when you don't want convention-based content extraction — same adapter resolution and source routing as `EntityAi`.
- **Per-category routing**: `Koan:Ai:Chat`, `Koan:Ai:Embed`, `Koan:Ai:Ocr` each take `Source` + `Model`; `Ocr` can route `Via: "Chat"` to a vision model. Override at the call site with `using (Client.Scope(chat: "fast-local"))`.
- **Recipes**: `Koan:Ai:ActiveRecipe` + `Koan:Ai:Recipes:{name}` bind named capability→model sets (sparse — missing keys mean "no opinion").
- **Typed extraction**: `Client.Chat<T>(prompt, ct)` and `Client.Extract<T>(content, ct)` deserialize a JSON-schema-constrained response into `T`.
- **Vector search & migration**: precompute the query vector with `Client.Embed`, then `Vector<T>.Search(float[], ...)`; reuse stored vectors across stores via `IVectorSearchRepository.ExportAll` / `Upsert`. Full detail in `koan-vector`.

## Agentic / RAG surface is migrating to Agyo (ARCH-0089)

`Koan.AI.Orchestration` and `Koan.AI.Agents` (multi-step chains, RAG, eval/review) are **real but being dissolved out of Koan** into the Agyo repo per [ARCH-0089](../../../docs/decisions/ARCH-0089-ai-pillar-dissolution.md). Koan keeps the **entity-AI core** (the 8 projects behind `Koan.AI` + `Koan.Data.AI`). For new agentic/RAG composition, target Agyo; don't build a new orchestration loop inside Koan.

## See also

- [Reference card: ai-data.md](../../../docs/reference/cards/ai-data.md) — one-screen pillar map
- [AI integration guide](../../../docs/guides/ai-integration.md) · [AI vector how-to](../../../docs/guides/ai-vector-howto.md)
- [`samples/S7.Meridian`](../../../samples/S7.Meridian/README.md) — `[MediaAnalysis]` + `[Embedding]` + entity-as-context chat (Ollama)
- [`samples/S5.Recs`](../../../samples/S5.Recs/README.md) — AI recommendation engine
- [ARCH-0089 — AI pillar dissolution](../../../docs/decisions/ARCH-0089-ai-pillar-dissolution.md) — entity-AI stays, agentic/RAG → Agyo
- Vector deep-dive: the `koan-vector` skill (`Vector<T>.Search(float[])`, export/import migration)
