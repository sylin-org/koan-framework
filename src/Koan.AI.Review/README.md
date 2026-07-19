# Sylin.Koan.AI.Review

Define typed human-review queues and apply approve, reject, edit, label, or flag decisions to reviewable Entities.

```bash
dotnet add package Sylin.Koan.AI.Review
```

## Meaningful use

The package automatically registers review infrastructure through `AddKoan()`. The application defines its business
queue explicitly with standard DI:

```csharp
builder.Services.AddKoan();
builder.Services.AddKoanReview(review => review.Queue<ArticleSummary>(
    "summary-review",
    queue => queue
        .Where(item => item.ReviewStatus == ReviewStatus.Pending)
        .Display(item => new { item.Title, item.GeneratedSummary })
        .Approve()
        .Reject(requireReason: true)
        .Edit(item => item.GeneratedSummary)
        .Flag("hallucination")));
```

`ArticleSummary` implements `IReviewable`. A controller, worker, or other authorized application boundary loads the
Entity and calls `IReviewActionHandler`; for example `ApproveAsync(entity, reviewerId, ct)`.

## Guarantees and limitations

- Queue definitions are typed, process-local configuration and require both `Where` and `Display`. Duplicate names for
  the same Entity type are rejected.
- The default handler mutates review fields in memory and validates requested edit/label properties. The caller owns
  authorization, reviewer identity, Entity load/save, concurrency, and any audit/event publication.
- Reference plus `AddKoan()` registers the registry and handler, but does not invent application queues or HTTP/UI
  endpoints. `AddKoanReview` is the deliberate business-queue declaration, not module activation.
- The package does not provide durable work assignment, locks, notifications, SLA/escalation, anonymous reviewer
  trust, or atomic persistence. Missing fields and invalid queue definitions fail with corrections.

See [TECHNICAL.md](TECHNICAL.md) for registration, action, and persistence ownership.
