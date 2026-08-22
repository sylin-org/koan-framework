---
type: GUIDE
domain: ai
title: "Answer from your own Entities (RAG)"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-08-19
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/guides/ai-rag-howto.md
related_guides:
  - ai-vector-howto.md
  - entity-capabilities-howto.md
  - jobs-howto.md
---

# Answer from your own Entities

"Answer questions from my data" is one outcome, but it is four separate pieces, and no single package
README owns the composition. This guide is the composition. The pieces stay separable on purpose —
you can change the model without touching the index, or the index without touching the domain.

| Piece | What it does | Package |
|---|---|---|
| Model provider | runs chat and embedding calls | `Sylin.Koan.AI` plus one connector |
| Embedding ownership | decides what text represents an Entity | `Sylin.Koan.Data.AI` |
| Vector index | stores and retrieves by meaning | one `Sylin.Koan.Data.Vector.Connector.*` |
| Chain | retrieves, then asks, then returns citations | `Sylin.Koan.AI.Orchestration` |

`Sylin.Koan.AI.Orchestration` and `Sylin.Koan.Data.AI` are **not assessed** — installable and
documented, with nothing promised about them. The [capability map](../reference/capability-map.md)
carries the current disposition of every piece named here.

## 1. Reference the pieces

Nothing else registers. `AddKoan()` stays exactly as it is.

```powershell
dotnet add package Sylin.Koan.AI
dotnet add package Sylin.Koan.AI.Connector.Ollama
dotnet add package Sylin.Koan.Data.AI
dotnet add package Sylin.Koan.Data.Vector.Connector.SqliteVec
dotnet add package Sylin.Koan.AI.Orchestration
```

Ollama and SqliteVec are the local, no-service choices. Swap the connector rows for a hosted provider
or a dedicated vector store; the domain code below does not change.

## 2. Say what represents the Entity

`[Embedding]` is what makes a save produce a vector. Neither the AI connector nor the vector store
supplies it — this attribute lives in `Sylin.Koan.Data.AI` and nothing else brings it in.

```csharp
[Embedding(Template = "{Title}. {Body}")]
public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int Year { get; set; }
}
```

The template is the retrieval contract. It decides what "similar" means, so choose the fields a reader
would actually match on and leave out identifiers and boilerplate.

## 3. Retrieve, then ask

`Chain.Create()` composes an immutable, typed pipeline. Steps run in declared order and cancellation
propagates.

```csharp
var result = await Chain.Create()
    .System("Answer only from the retrieved articles. If they do not contain the answer, say so.")
    .Retrieve<Article>(question, topK: 5)
    .Chat("Question: {question}")
    .Run(new { question }, ct);

Console.WriteLine(result.Text);
```

`Run` takes the variables the templates interpolate and returns a `ChainResult`.

## 4. Show your work

Citations are first-class, not something you reconstruct afterwards. `ChainResult.Citations` is
populated whenever the chain retrieved (and is `null` when it did not):

```csharp
foreach (var citation in result.Citations ?? [])
{
    Console.WriteLine($"{citation.Source} ({citation.Relevance:P0}): {citation.Excerpt}");
}
```

`Citation` is `(string Source, string Excerpt, double Relevance)`. A grounded answer a user can check
is the difference between a demo and something you can put in front of a customer, so surface these
rather than discarding them. `result.Metrics` carries the execution metrics for the same run.

## 5. Narrow the retrieval when the question implies it

`Retrieve<T>` takes an optional predicate over the Entity, compiled by the same
`LinqFilterCompiler` as `Entity<T>.Query(predicate)`:

```csharp
.Retrieve<Article>(question, topK: 5, filter: a => a.Year >= 2024)
```

A predicate that cannot be lowered to the provider **fails loudly at retrieval** rather than returning
a silently-empty result. That is deliberate: an empty answer that looks like "nothing matched" is the
most expensive failure mode in retrieval, so Koan refuses to produce one.

`topK` bounds the work. `rerank: true` and `alpha` are available where the provider supports them —
check the connector's README before assuming either.

## 6. Beyond the straight line

The builder is immutable, so these compose without surprising each other. `Parse<T>()` turns output
into a typed object; `Classify`, `Rerank`, `Compress`, and `Moderate` are steps; `WithTools(...)`,
`WithMemory(...)`, and `Scope(...)` are explicit choices; streaming yields `ChainChunk`. The
[package README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.AI.Orchestration/README.md)
owns the full step vocabulary.

## What this does not do

State these before shipping, because each is a real boundary and none of them is hidden:

- **A chain is in-process composition, not a workflow.** No retries, compensation, transactions,
  scheduling, or human approval. Put durable work behind [Jobs](jobs-howto.md) and human sign-off
  behind `Sylin.Koan.AI.Review`.
- **Referencing AI does not create embeddings, acquire a model, or provision an index.** Adding the
  packages makes the capability available; the model artifact and the index remain operational facts.
- **Retrieval requires the Entity's vector and embedding path to actually work.** Chat steps require a
  chat-capable provider. Missing either fails with a correction rather than a fallback answer.
- **Re-embedding existing rows is separate work.** `[Embedding]` governs saves from that point on.

## Prove it

Three claims, narrowest credible evidence for each:

1. **Behavior** — index two articles with clearly different subjects, ask a question only one answers,
   and assert the right one is cited first.
2. **Composition** — assert the intended provider and vector store actually participated, via
   `/.well-known/Koan/facts` or `koan.lock.json`. A passing answer proves a model replied, not that
   your composition is the one you meant.
3. **Correction** — stop the provider, or pass a filter the store cannot lower, and assert the failure
   surfaces at the owning boundary instead of returning an empty or invented answer.

A relevance assertion on a known-neighbor pair is worth more than a snapshot of generated prose, which
will drift with the model.
