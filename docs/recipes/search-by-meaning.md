---
type: RECIPE
recipe: search-by-meaning
title: "Search by meaning"
domain: ai
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/search-by-meaning.md
gets_you: "Find Entities similar to a phrase, not a keyword."
works_if: "The application has Entity types carrying text worth matching on."
costs: "Runs entirely offline on the local path. The durable local index adds no service to operate."
ingredients:
  - "one | AI runtime | Sylin.Koan.AI"
  - "one-or-more | model runtime, user's choice | Sylin.Koan.AI.Connector.Ollama, Sylin.Koan.AI.Connector.Onnx, Sylin.Koan.AI.Connector.LMStudio, Sylin.Koan.AI.Connector.HuggingFace"
  - "one | embedding ownership on the Entity | Sylin.Koan.Data.AI"
  - "one | vector runtime | Sylin.Koan.Data.Vector"
  - "one | vector index, user's choice | Sylin.Koan.Data.Vector.Connector.SqliteVec, Sylin.Koan.Data.Vector.Connector.InMemory, Sylin.Koan.Data.Vector.Connector.PgVector, Sylin.Koan.Data.Vector.Connector.RedisVector, Sylin.Koan.Data.Vector.Connector.MongoAtlasVector, Sylin.Koan.Data.Vector.Connector.Qdrant, Sylin.Koan.Data.Vector.Connector.Weaviate, Sylin.Koan.Data.Vector.Connector.Milvus"
absent:
  - "hosted frontier embedding model | no OpenAI, Anthropic, or Gemini connector exists | run embeddings locally, or call the vendor directly with an HttpClient and store the vector yourself"
---

# Search by meaning

A save produces a vector; a query compares vectors. The developer keeps writing `Article`.

## Choosing the pieces

**Where the model runs.** Ollama is the usual answer — no key, nothing leaves the machine, costs RAM
and disk, and someone has to pull the model. ONNX runs in-process, which is the right answer when
"don't make me run another container" is the real constraint; it suits embeddings particularly well.
LM Studio fits a human who wants to swap models by hand. HuggingFace is the only hosted connector and
is **not assessed**.

**Where vectors live.** Weigh these against the deployment they described, not against a feature
matrix:

| | Adds a process to run | Survives restart | Fits when |
|---|---|---|---|
| InMemory | no | no | they are still exploring and will say so |
| SqliteVec | no | yes | a real single-node application — the usual answer |
| PgVector | no new process | yes | they already operate Postgres with the `vector` extension |
| RedisVector | no new process | yes | they already operate Redis with Search/vector support; plain Redis is insufficient |
| MongoAtlasVector | no new process | yes | they already operate Atlas with Vector Search; ordinary MongoDB is insufficient |
| Qdrant · Weaviate · Milvus | yes | yes | they already operate one; do not introduce a second |
| Elasticsearch · OpenSearch | yes | yes | they already run it; search-engine-backed, so do not promise dedicated-vector behavior |

If they already run a vector service, use it. Adding a second store to an application that has one is
a cost with no matching benefit.

## Assembly

```powershell
dotnet add package Sylin.Koan.AI
dotnet add package Sylin.Koan.AI.Connector.Ollama
dotnet add package Sylin.Koan.Data.AI
dotnet add package Sylin.Koan.Data.Vector.Connector.SqliteVec
```

`Sylin.Koan.Data.AI` is what makes a save produce a vector — nothing else brings it in, and it is
**not assessed**.

```csharp
[Embedding(Template = "{Title}. {Body}")]
public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}
```

The template *is* the retrieval contract: it decides what "similar" means. Choose the fields a reader
would match on; leave out identifiers and boilerplate.

For a model with many fields, take everything and subtract instead — and move the work off the save:

```csharp
[Embedding(
    Policy = EmbeddingPolicy.AllStrings,
    Async = true,
    Model = "nomic-embed-text",
    Version = 2,
    Exclude = ["EventId", "InferredStyleId"])]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    public float[]? Embedding { get; set; }   // Koan recognizes float[] as the search vector
}
```

`Async = true` keeps embedding off the request. **`Version` is not decoration:** stored and query
embeddings must occupy the same vector space, so changing the model means bumping the version and
re-embedding. `Exclude` matters more than it looks — identifier fields swept in by `AllStrings` are
noise that quietly degrades every result.

**Set the embedding model explicitly.** Chat and Embed are separate categories, and a chat or vision
model asked to embed fails at the provider. This is the single most common way an otherwise-correct
addition breaks:

```json
{ "Koan": { "Ai": {
  "Chat":  { "Model": "qwen2.5vl" },
  "Embed": { "Model": "nomic-embed-text" }
} } }
```

Each category takes `Source`, `Model`, `Via`, and `Fallback` independently.

Depth: [AI and vector how-to](../guides/ai-vector-howto.md) ·
[embedding best practices](../guides/embedding-best-practices.md).

## Prove it

1. **Behavior** — save two Entities with clearly different subjects, search for one, assert it ranks
   first. A known-neighbour assertion survives model changes; a snapshot of generated text does not.
2. **Composition** — assert the intended vector provider actually won, via `/.well-known/Koan/facts`
   or `koan.lock.json`. A passing search proves something replied, not that your composition is the
   one you meant.
3. **Correction** — stop the model runtime, or pass a filter the store cannot lower, and assert the
   failure surfaces rather than returning a misleadingly empty result.

## Boundaries

- Referencing these packages does not acquire a model artifact or provision an index.
- `[Embedding]` governs saves from that point on. **Rows already saved are not embedded** — re-embedding
  existing data is separate work.
- Dimensions belong to the model. Changing the embedding model invalidates the existing index.

## Interacts with

**Tenancy.** If this application is multi-tenant, embedding work that runs in the background crosses an
async boundary and must carry the ambient tenant, or it reads nothing and silently indexes nothing.
