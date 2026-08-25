---
type: REFERENCE
domain: ai
title: "Human review for AI output"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/ai/review.md - cold-executed end-to-end against published packages
    (feed probe): typed queue registration via AddKoanReview, approve/reject/edit/label/flag applied
    through IReviewActionHandler and persisted over SQLite, duplicate-name and missing-Where refused,
    queue predicate selecting only Pending rows
---

# Human review for AI output

Model output waits in a typed queue until a person approves, edits, rejects, labels, or flags it.
The queue records decisions; your read path publishes only what was approved.

## You need

| Piece | Package | Note |
|---|---|---|
| Review queues + action handler | `Sylin.Koan.AI.Review` | registers its infrastructure through `AddKoan()` |
| Any Entity store | one data connector | decisions persist through ordinary `Entity<T>` saves |

Verified against: `Sylin.Koan.AI.Review` 1.0.6 or newer, `Sylin.Koan.App` 1.0.7 or newer,
`Sylin.Koan.Data.Connector.Sqlite` 1.0.12 or newer (patch releases compatible).

> **The queue does not decide who may decide.** Authorization, reviewer identity, loading and saving
> the Entity are the application's job - `IReviewActionHandler` mutates review fields in memory and
> you persist. Make the *Pending* state the one your public read path filters on; reviewing output
> the app already published is theatre.

## The constraint box

> Two sharp edges to know before you build on this:
>
> - `Reject(requireReason: true)` is a **declaration**, not an enforcement point. The handler accepts
>   a null reason regardless. Enforce reason-required rejections where you call the handler.
> - `FlagAsync` appends to an entity property named `Flags` only when it exists, is a
>   `List<string>`, **and is non-null**. A null list silently drops the flag entry (the status still
>   becomes `Flagged`). Initialize `Flags = [];` on entities you intend to flag.

## Assembly

```csharp
using Koan.AI.Review;
using Koan.Data.Core;
using Koan.Data.Core.Model;

public sealed class ArticleSummary : Entity<ArticleSummary>, IReviewable
{
    public string Title { get; set; } = "";
    public string GeneratedSummary { get; set; } = "";
    public string? OriginalGeneratedSummary { get; set; }
    public string? Category { get; set; }
    public List<string>? Flags { get; set; } = [];

    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}
```

Declare the business queue in `Program.cs`. `AddKoan()` comes from `using Koan.Core;`;
each `AddKoanReview` call configures **one** registry - duplicate names are refused inside that call,
so declare all your queues together:

```csharp
using Koan.Core;
using Koan.AI.Review;

builder.Services.AddKoan();
builder.Services.AddKoanReview(review => review.Queue<ArticleSummary>(
    "summary-review",
    queue => queue
        .Where(item => item.ReviewStatus == ReviewStatus.Pending)
        .Display(item => new { item.Title, item.GeneratedSummary })
        .Approve()
        .Reject(requireReason: true)
        .Edit(item => item.GeneratedSummary)
        .Label(item => item.Category, "tech", "farm")
        .Flag("hallucination")));
```

Apply a decision wherever an authorized reviewer acts - reviewer id comes before the payload on every
verb (`RejectAsync(entity, reviewedBy, reason)`, not reason-first):

```csharp
using Koan.AI.Review;

var handler = app.Services.GetRequiredService<IReviewActionHandler>();
await handler.ApproveAsync(summary, "leo");
await handler.RejectAsync(summary, "leo", reason: "invents a claim");
await handler.EditAsync(summary, nameof(ArticleSummary.GeneratedSummary), "new text", "leo");
await summary.Save();   // persistence is yours (Save comes from Koan.Data.Core)
```

`EditAsync` sets `ReviewStatus.Edited` and captures the prior value into an `Original{FieldName}`
property when one exists. `LabelAsync` is additive and leaves status untouched.

## Prove it

The registry is public and DI-resolvable, which makes composition assertable:

```csharp
using Koan.AI.Review;

var registry = app.Services.GetRequiredService<ReviewQueueRegistry>();
var queue = registry.Get<ArticleSummary>("summary-review"); // .Names lists every queue
```

Duplicate queue names are rejected at declaration time - inside the same `AddKoanReview` call, with
"A review queue named '...' is already registered" - and a queue without `Where(...)` or `Display(...)`
fails right there naming the missing half. Both are startup-time refusals, not runtime checks.

## Do not, at this level

- Do not treat the queue as authorization, audit storage, or a workflow engine - no locks, SLAs,
  notifications, or durable assignment come with it.
- Do not rely on prompt wording to keep bad output unpublished; filter reads on the reviewed state.
- Do not register queues before `AddKoan()` - declare them after, as above.

## Leaves

- **Pasteable build:** [review AI output](../../recipes/review-ai-output.md) - when this is the answer,
  install, prove-it legs
- **Deep contract:** [AI.Review TECHNICAL](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.AI.Review/TECHNICAL.md)
