---
type: REF
domain: ai
title: "AI (entity-aware) — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.19.0
validation:
  date_last_tested: 2026-07-16
  status: verified
  scope: docs/reference/cards/ai-data.md
---

# AI (entity-aware) — pillar map

> One-screen map of the entity-aware AI surface — the load-bearing slice of the AI pillar. Full detail: [ai/index.md](../ai/index.md).

**What it does** — Runs AI operations directly over your `Entity<T>` models without leaving the entity flow. `EntityAi.Embed(entity)` / `.Chat(msg, entity)` / `.Ocr(entity)` infer their content by convention (the `[Embedding]` chain, then `AllStrings`, then JSON), so the entity *is* the prompt. The provider is chosen by package reference — add `Koan.AI.Connector.Ollama` (the default inference backend) or `Koan.AI.Connector.LMStudio` and that adapter becomes the runtime (Reference = Intent); no client to wire, no provider-specific code.

## The one canonical pattern

Annotate the entity with `[Embedding]` to opt into auto-embed-on-save, then call the static `EntityAi` verbs on demand. Entity verbs stay **Save / Remove / Query**; the AI verbs are `Embed` / `Chat` / `Ocr`.

```csharp
[Embedding(Template = "{Title}\n\n{Content}")]
public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

var article = new Article { Title = "Koan", Content = "Entity-first AI." };
await article.Save();                                    // embedding generated on save

var vector  = await EntityAi.Embed(article);             // on-demand embedding
var answer  = await EntityAi.Chat("Summarize this", article); // entity as context
```

`EntityAi` works with or without `[Embedding]` — the attribute gates the auto-embed-on-save lifecycle, not the on-demand verbs.
The generated vector lives in the selected vector store; Koan does not mutate a `float[]` property on
the Entity. With `Async = true`, the durable worker reloads the current Entity in its captured logical
context and writes through the same vector-only boundary as synchronous lifecycle and migration.

There is intentionally no `article.AI.Index()` or collection `Embed()` grammar today. Ordinary index
intent is `[Embedding]` + `Save`; explicit selected-set and whole-collection rebuilds belong to
`EmbeddingMigrator`. `EntityAi.Embed(article)` remains the scalar, on-demand transform until a real
application proves a distinct source-level result and failure contract.

## ≤5 attributes you'll use

| Attribute | What it does |
|---|---|
| `[Embedding]` · `[Embedding(Template="{A}\n{B}")]` | Opt the entity into embedding; pick content by `Template` / `Properties` / `Policy`, with optional `Async`, `Model`, `Source`. |
| `[EmbeddingIgnore]` | Exclude a property from convention-based embedding content. |
| `[EmbedStorage(Partition="…", Source="…")]` | Route the background `EmbedJob` rows to a dedicated partition / adapter. |
| `[MediaAnalysis(Analysis = MediaAnalysis.Describe \| MediaAnalysis.Ocr)]` | Auto-run vision / OCR / transcription on a `MediaEntity` upload; results feed into `[Embedding]` text. |

## The escape hatch

When you don't want the entity convention, drop to the lower-level `Koan.AI` contracts — the static `Client` chat / embeddings surface speaks raw strings:

```csharp
var reply  = await Client.Chat("Explain GUID v7 in one line");
var vector = await Client.Embed("free-form text, no entity");
```

Same adapter resolution and source routing as `EntityAi`; you just give up the convention-based content extraction. For multi-step composition see the orchestration surface in [ai/index.md](../ai/index.md).

## The sample that shows it

[`samples/S7.Meridian`](../../../samples/S7.Meridian/README.md) — a document-intelligence workbench: `[MediaAnalysis]` extraction over uploaded PDFs/images, `[Embedding]` + hybrid vector search, and entity-as-context chat backed by an Ollama provider.
