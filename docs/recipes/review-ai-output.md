---
type: RECIPE
recipe: review-ai-output
title: "Review AI output before it ships"
domain: ai
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/review-ai-output.md
gets_you: "Model output waits in a queue until a person approves, edits, or rejects it."
works_if: "Something in the application produces output a person would want to check."
costs: "Adds no service. Adds a human step, which is the point — plan for who does the reviewing."
ingredients:
  - "one | review queues | Sylin.Koan.AI.Review"
  - "optional | durable background work feeding the queue | Sylin.Koan.Jobs"
---

# Review AI output before it ships

A typed queue over a reviewable Entity, with approve / reject / edit / label decisions.

## When this is the answer

Offer this one. Developers rarely ask for it by name, and most production AI features need it: the
model drafts, a person signs off, the customer sees only what was signed off.

Reach for it whenever model output becomes something a customer reads, a record the business relies
on, or an action with consequences. Skip it when output is advisory and clearly labelled as such, or
when a deterministic check can validate the result better than a human can.

**The real question is not technical.** Ask who reviews, how quickly, and what happens to the backlog
when nobody does — a review queue nobody works is an outage with extra steps. If the answer is "no
one", the honest recommendation is a narrower AI feature rather than a queue.

## Assembly

```powershell
dotnet add package Sylin.Koan.AI.Review
```

**Not assessed.** Review infrastructure registers through `AddKoan()`; the application declares its
business queue explicitly:

```csharp
builder.Services.AddKoan();
builder.Services.AddKoanReview(review => review.Queue<ArticleSummary>(
    "summary-review",
    queue => queue
        .Where(item => item.ReviewStatus == ReviewStatus.Pending)
        .Display(item => new { item.Title, item.GeneratedSummary })
        .Approve()
        .Reject(requireReason: true)));
```

Keep generated output in a pending state until a decision moves it, and make the *pending* state the
one the read path filters on. A queue that reviews output the application has already published is
theatre.

## Prove it

1. **Behavior** — generated output lands pending; approval publishes it; rejection does not.
2. **Composition** — assert the queue is registered and its predicate selects what you expect.
3. **Correction** — assert that unreviewed output is not reachable through the public read path,
   including any projection that bypasses the obvious query.

## Boundaries

- A queue records decisions; it does not decide who may make them. Authorization is separate.
- It does not retry, schedule, or escalate a stale item on its own.
- It does not make the model correct — it makes a person accountable for shipping the output.

## Interacts with

**Jobs.** Generation usually belongs in durable background work, with the queue as its receipt.

**Agents.** A writing agent plus a review queue is the safe form of "let it do things for me": the
model proposes the change, a person commits it.
