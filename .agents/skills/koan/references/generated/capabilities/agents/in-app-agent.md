---
type: REFERENCE
domain: mcp
title: "In-application Entity agent"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/agents/in-app-agent.md
---

# In-application Entity agent

Let application code ask a model to choose among bounded Entity reads, searches, or named tools over
several steps. This is orchestration inside the application, not an MCP endpoint for another client.

## You need

| Piece | Package | Note |
|---|---|---|
| AI runtime | `Sylin.Koan.AI` | owns provider-neutral model operations |
| One local chat connector | `Sylin.Koan.AI.Connector.Ollama` or `Sylin.Koan.AI.Connector.LMStudio` | hosted frontier connectors do not ship |
| Agent runtime | `Sylin.Koan.AI.Agents` | installable, not yet assessed |
| Semantic Entity retrieval (optional) | `Sylin.Koan.Data.AI` plus a vector runtime and connector | required by `WithSearch<T>()` |

## The constraint box

> **The constraint:** Generated tools reach real application data. `WithEntities<T>()` starts with
> reads; `write: true` is an authority decision, not a convenience flag. Prompt wording cannot replace
> authorization, tenancy, idempotency, iteration/token bounds, or human review. Small local models may
> be unreliable at multi-step planning, and the agent runtime is not yet assessed.

## Choose the boundary first

| Need | Route |
|---|---|
| One retrieval followed by one grounded answer | [answer from your own data](../../recipes/answer-from-my-data.md) |
| Several model-chosen application steps | in-application Entity agent |
| An external client must discover and call the app | [Entity MCP surface](entity-mcp.md) |
| Work must survive restart or run on a schedule | [background Job](../work/background-jobs.md); invoke bounded AI inside it |
| A model-produced write needs approval | [review AI output](../../recipes/review-ai-output.md) |

## Leaves

- **Decision guide with assembly; not yet assessed:** [let an agent act](../../recipes/let-an-agent-act.md)
- **Entity hook map:** [Entity capability hooks](../data/entities.md)
- **Runnable bounded-agent counterpart:** [GoldenJourney](https://github.com/sylin-org/koan-framework/blob/main/samples/GoldenJourney/README.md)
