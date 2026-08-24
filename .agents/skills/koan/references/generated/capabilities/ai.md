---
type: REFERENCE
domain: ai
title: "AI capabilities"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/ai.md
---

# AI capabilities

Koan's AI is local-first: connectors run in-process or on your machine, hosted frontier-model
connectors deliberately do not ship, and embedding width is measured from your first document.
Pick the capability; the node or leaf carries the constraints.

- **Semantic search** - index entities by meaning, query by intent:
  [semantic-search](ai/semantic-search.md)
- **Answer from your own data (RAG)** - grounded answers with citations; builds on semantic search:
  [answer-from-my-data](../recipes/answer-from-my-data.md)
- **Chat and completion** - provider-neutral completion through one client facade:
  [AI reference](../reference/ai/index.md)
- **Vision** - read and reason over images:
  [read-an-image](../recipes/read-an-image.md)
- **Review human-in-the-loop** - approve, reject or edit AI output before it lands:
  [review-ai-output](../recipes/review-ai-output.md)
- **Reasoning and orchestration** - RAG chains, branching, structured output
  (installable, not yet assessed): [AI.Orchestration README](../../../src/Koan.AI.Orchestration/README.md)

Constraints that span steps live on the capability nodes - read them before writing code.

## Standing constraints

- Local-first is the identity: connectors run in-process or on your machine, and hosted
  frontier-model connectors deliberately do not ship.
- `Sylin.Koan.Data.AI` owns `[Embedding]` - no other package brings it in transitively; reference
  it explicitly whenever a save should produce a vector.
- Capabilities marked *not assessed* on the product surface carry no guarantees.

## Do not, at this level

- Do not promise hosted-model behavior, quote per-token pricing, or name connectors that do not
  exist.
- Do not mix embedding models between indexing and querying - the constraint lives on the
  semantic-search node and it is absolute.
