---
type: REFERENCE
domain: ai
title: "Semantic search"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/ai/semantic-search.md
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

## The constraint box

> **One model, everywhere.** The same embedding model and its dimensions must serve both the
> stored vectors and every query. The width is measured from your first indexed document; mixing
> models between indexing and search invalidates results silently. Pick from the table, then stay
> with it until you are ready to re-index.

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

## Leaves

- **Pasteable build:** [search-by-meaning](../../recipes/search-by-meaning.md) - install, attribute,
  query endpoint, provider limits
- **Runnable exemplar:** GardenCoop chapter 2 - entity with
  [Embedding](https://github.com/sylin-org/koan-framework/blob/main/samples/journeys/GardenCoop/02-LocalDiscovery/Models/Produce.cs),
  [search controller](https://github.com/sylin-org/koan-framework/blob/main/samples/journeys/GardenCoop/02-LocalDiscovery/Controllers/ProduceSearchController.cs),
  [search page](https://github.com/sylin-org/koan-framework/blob/main/samples/journeys/GardenCoop/02-LocalDiscovery/wwwroot/index.html)
- **Deep contract:** [AI and vector search how-to](../../guides/ai-vector-howto.md)
