---
type: RECIPE
recipe: let-an-agent-act
title: "Let an agent act on my data"
domain: ai
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/let-an-agent-act.md
gets_you: "A model that can look things up in the application — and, if you allow it, change them."
works_if: "The application has Entity types the model should be allowed to reach."
costs: "Runs offline with a local chat model. Each request may cost several model calls, not one."
ingredients:
  - "one | AI runtime | Sylin.Koan.AI"
  - "one-or-more | chat model runtime, user's choice | Sylin.Koan.AI.Connector.Ollama, Sylin.Koan.AI.Connector.LMStudio"
  - "one | agent runtime | Sylin.Koan.AI.Agents"
  - "optional | semantic retrieval for the agent's tools | Sylin.Koan.Data.AI, Sylin.Koan.Data.Vector"
absent:
  - "hosted frontier model | no OpenAI, Anthropic, or Gemini connector exists | run locally, accepting that small models plan worse over multiple steps"
---

# Let an agent act on my data

Tools are generated from `Entity<T>`. Read tools by default; write is a deliberate decision.

## When this is the answer

"Let it look things up for me", "answer questions and then file the ticket". Reach for it when the
useful work needs *several* steps chosen at runtime. If one retrieval and one answer would do,
[answer questions from my own data](answer-from-my-data.md) is cheaper and far more predictable.

**Raise the write question explicitly, early.** `WithEntities<T>()` exposes read tools; `write: true`
lets a model create, update, and delete rows. That is a security conversation, not a flag — name the
Entities and say out loud what the model would be permitted to change. Most applications should start
read-only and add writes behind an approval step.

Multi-step planning is where small local models struggle most. If the developer expects reliable
multi-step behavior, the absent ingredient above is the honest constraint.

## Assembly

```powershell
dotnet add package Sylin.Koan.AI.Agents
```

**Not assessed.**

```csharp
var result = await Agent.Create()
    .System("Answer from the product catalog.")
    .WithEntities<Product>()
    .WithSearch<Product>()
    .WithMaxIterations(6)
    .Run("Which products suit a small studio?", ct: ct);
```

`WithSearch<T>()` needs the Entity's embedding and vector path. `WithTools(...)`, `WithMemory(...)`,
`Scope(...)`, token limits, and streamed `AgentStep` output are all explicit choices.

## Prove it

1. **Behavior** — a question that requires two lookups; assert the final answer and that the expected
   tools were called.
2. **Composition** — assert the intended chat provider and the Data providers behind the tools
   participated.
3. **Correction** — remove a tool's provider and assert the failure surfaces; with writes enabled,
   assert an unauthorized write is refused at the authorization boundary, not by prompt wording.

## Boundaries

- Iteration and token limits bound *planning*, not external tool latency or provider cost.
- No durable workflow, human approval, sandboxing, scheduling, or cross-tool atomicity.
- Tool handlers must be authorized, idempotent where replay matters, and safe against untrusted model
  arguments. The agent does not invent fallback data, and it does not make an unsafe handler safe.

## Interacts with

**Authorization and tenancy.** Generated tools reach real data. Whatever rule governs a user's HTTP
access must govern the agent's tools, or the model becomes a way around it.

**Human review.** Writes plus [review before it ships](review-ai-output.md) is usually the shape a
production application actually wants.
