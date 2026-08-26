---
type: REFERENCE
domain: ai
title: "Semantic search"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/ai/semantic-search.md - cold-executed on the Ollama path against published
    packages (feed probe): attribute-only save→embed→SqliteVec, ranked search over HTTP both
    directions with clear separation, composition via facts, corrective failure naming a missing model
---

# Semantic search

Index entities by what they *mean* and query them by intent - "something quick before the game"
finds the right chores without a keyword in common.

## You need

| Piece | Package | Note |
|---|---|---|
| The `[Embedding]` attribute and save-time indexing | `Sylin.Koan.Data.AI` | nothing else brings it in |
| One embedding-capable adapter | scale table below | in-process, no service |
| One vector store | scale table below | pairs with your data store's engine where possible |

Verified against: `Sylin.Koan.Data.AI` 1.0.11 or newer, `Sylin.Koan.Data.Vector.Connector.SqliteVec` 1.0.6 or newer, `Sylin.Koan.AI.Connector.Onnx` 1.0.4 or newer, `Sylin.Koan.AI.Connector.Ollama` 1.0.8 or newer (patch releases compatible).

> **Discovery fails soft at boot, hard at first save.** If the model runtime is unreachable during
> startup, composition logs a warning and continues; the wall appears at your first Entity save as
> "No AI sources available". When in doubt — slow host, IPv6-only resolution, container networking —
> set `Koan:Ai:Ollama:Endpoints` explicitly and keep serving the same embedding model.

## The constraint box

> **One model, everywhere.** The same embedding model and its dimensions must serve both the
> stored vectors and every query. The width is measured from your first indexed document; mixing
> models between indexing and search invalidates results silently. Pick from the table, then stay
> with it until you are ready to re-index.
>
> A *dimension* mismatch on a durable store is caught loudly at first write — SqliteVec validates
> the space shape and refuses with the expected dimensions and metric. The silent case is the
> nastier one: a different model with the **same width**. Switching models means re-indexing, always.

## Choose by scale

| Scale | Variant | What it means |
|---|---|---|
| Portable offline bundle | [embedding/portable](embedding/portable.md) | in-process ONNX; model and vocabulary sidecars ride with the app; air-gap friendly |
| Local with a model server | Ollama connector | [README](https://github.com/sylin-org/koan-framework/blob/main/src/Connectors/AI/Ollama/README.md) - Koan discovers a local Ollama automatically, but discovery has a short readiness timeout: if the service answers slowly, configure the endpoint explicitly (`Koan:Ai:Ollama:Endpoints`, per the README) and keep the served embedding model consistent |
| Hosted / remote | **does not ship** | Koan AI is local-first; hosted frontier-model connectors are deliberately absent. An OpenAI-spec-compatible gateway is unassessed territory, not a supported path |

## Do not, at this level

- Do not mix embedding models or dimensions between the indexing path and the query path - the
  index invalidates silently.
- Do not add vector-space wiring lambdas "to be safe" - attribute-only composition is the
  supported path; if saves report "no declared space", the owned flow above owns the fix.
- Do not hand-write embedding pipelines beside `Client.Embed`.

## The type-scoped shortcut: `Entity.AI`

For applications that just want semantic search over one Entity kind, the four-step dance —
embed the query, `Vector<T>.Search`, fan out `Get`, map results — collapses into the type
gateway. `Sylin.Koan.Data.AI` 1.0.13 or newer delivers `YourEntity.AI.*` to every Entity kind:

```csharp
using Koan.Data.AI;   // the gateway lives here

// Save as usual - the [Embedding] attribute indexes it (see above).
await new Produce { Name = "Cherry tomatoes", Description = "sweet, quick" }.Save(ct);

// Search by meaning - embeds the query, finds nearest vectors, loads the entities.
var produce = await Produce.AI.Search("something quick before the game", limit: 10);

// With similarity scores attached:
var scored = await Produce.AI.SearchScored("something quick", limit: 10);
// scored[i].Entity / scored[i].Similarity

// Embed one instance through the kind's declared model + source:
var vector = await Produce.AI.Embed(produce);

// Find entities similar to one instance (excludes itself by default):
var similar = await Produce.AI.Similar(produce, limit: 10);
```

The gateway is bound to the same `[Embedding]` declaration as indexing — model, source and
dimensions route automatically, so the one-model constraint above is enforced by construction.
`Search` is convention-first (works without the attribute); the attribute remains the authority
for indexing behavior. Usings: `Koan.Data.AI` for the gateway; your model's regular usings.

## Leaves

- **Pasteable build:** [search-by-meaning](../../recipes/search-by-meaning.md) - install, attribute,
  query endpoint, provider limits
- **Runnable exemplar:** GardenCoop chapter 2 - entity with
  [Embedding](https://github.com/sylin-org/koan-framework/blob/main/samples/journeys/GardenCoop/02-LocalDiscovery/Models/Produce.cs),
  [search controller](https://github.com/sylin-org/koan-framework/blob/main/samples/journeys/GardenCoop/02-LocalDiscovery/Controllers/ProduceSearchController.cs),
  [search page](https://github.com/sylin-org/koan-framework/blob/main/samples/journeys/GardenCoop/02-LocalDiscovery/wwwroot/index.html)
- **Deep contract:** [AI and vector search how-to](../../guides/ai-vector-howto.md)
