---
type: RECIPE
recipe: answer-from-my-data
title: "Answer questions from my own data"
domain: ai
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/answer-from-my-data.md
gets_you: "A grounded answer to a natural-language question, with citations the reader can check."
works_if: "The application has Entity types carrying text, and can also do search by meaning."
costs: "Runs offline on the local path. Every question costs one retrieval plus one model call."
ingredients:
  - "one | everything from search by meaning | Sylin.Koan.AI, Sylin.Koan.Data.AI, Sylin.Koan.Data.Vector"
  - "one-or-more | model runtime, user's choice | Sylin.Koan.AI.Connector.Ollama, Sylin.Koan.AI.Connector.Onnx, Sylin.Koan.AI.Connector.LMStudio"
  - "one | chain composition | Sylin.Koan.AI.Orchestration"
absent:
  - "hosted frontier chat model | no OpenAI, Anthropic, or Gemini connector exists | run a local chat model, or call the vendor directly with an HttpClient"
---

# Answer questions from my own data

Retrieve first, then ask, and return what was retrieved alongside the answer. The citations are the
point — an answer nobody can check is a demo.

## When this is the answer

Reach for this when the developer says "chat with my documents", "answer support questions from our
articles", or "explain what's in this data". If they only want *finding* rather than *answering*,
[search by meaning](search-by-meaning.md) is smaller and cheaper — do not add a chat model to a
problem that does not need one.

The model choice matters more here than for search alone: retrieval quality is bounded by the
embedding model, but answer quality is bounded by the chat model, and small local chat models
hallucinate more when the retrieved context is thin. If the developer needs a frontier model, say so
now — see the absent ingredient above.

## Assembly

Everything from [search by meaning](search-by-meaning.md), plus:

```powershell
dotnet add package Sylin.Koan.AI.Orchestration
```

**Not assessed.**

```csharp
var result = await Chain.Create()
    .System("Answer only from the retrieved articles. If they do not contain the answer, say so.")
    .Retrieve<Article>(question, topK: 5)
    .Chat("Question: {question}")
    .Run(new { question }, ct);

Console.WriteLine(result.Text);

foreach (var citation in result.Citations ?? [])
    Console.WriteLine($"{citation.Source} ({citation.Relevance:P0}): {citation.Excerpt}");
```

`Citation` is `(string Source, string Excerpt, double Relevance)`. Surface these rather than
discarding them.

Narrow retrieval when the question implies it — `Retrieve<Article>(question, topK: 5, filter: a => a.Year >= 2024)`.
A predicate the provider cannot lower **fails loudly** rather than returning an empty result, which is
deliberate: a silent empty answer is the most expensive failure mode in retrieval.

The builder is immutable; `Parse<T>()`, `Classify`, `Rerank`, `Compress`, `Moderate`, `WithTools`,
`WithMemory`, `Scope`, and streamed `ChainChunk` compose without surprising each other.

Depth: [RAG how-to](../guides/ai-rag-howto.md).

## Prove it

1. **Behavior** — index two documents where only one answers a question; assert that one is cited
   first. Assert on the citation, never on the prose, which drifts with the model.
2. **Composition** — assert the intended chat provider and vector store participated.
3. **Correction** — ask something the corpus does not cover and assert it declines rather than
   inventing; stop the provider and assert the failure surfaces.

## Boundaries

- A chain is in-process composition, **not a workflow**: no retries, compensation, transactions,
  scheduling, or human approval.
- Chat steps need a chat-capable provider; retrieval needs the Entity's embedding and vector path.
  Missing either fails with a correction rather than a fallback answer.
- Nothing here reviews the output before a user sees it. If that matters, add
  [review before it ships](review-ai-output.md).

## Interacts with

**Tenancy.** Retrieval must be tenant-scoped or one customer's question can be answered from another's
documents. Verify the scope reaches the vector query, not only the Entity query.
