# Koan.AI.Review

Human-in-the-loop review queues for Koan. Define review queues over any entity, surface approve/reject/edit/label/flag actions, and close the feedback loop back to training data.

- Target framework: net10.0
- License: Apache-2.0
- Version: 0.6.3

## Install

```powershell
dotnet add package Sylin.Koan.AI.Review
```

## Quick Start

```csharp
// Define a review queue for AI-generated summaries
var queue = Review.Create<ArticleSummary>()
    .Named("summary-review")
    .Filter(x => x.Status == SummaryStatus.PendingReview)
    .Display(x => new { x.Title, x.GeneratedSummary, x.Source })
    .Approve(x => x.Status = SummaryStatus.Approved)
    .Reject(x => x.Status = SummaryStatus.Rejected)
    .Edit(x => new { x.GeneratedSummary });

// Process a review action (e.g., from API handler)
await Review.Approve<ArticleSummary>(summaryId, ct);
await Review.Reject<ArticleSummary>(summaryId, reason: "Inaccurate", ct);
await Review.Edit<ArticleSummary>(summaryId, patch: new { GeneratedSummary = "corrected text" }, ct);
await Review.Label<ArticleSummary>(summaryId, labels: ["high-quality", "technical"], ct);
await Review.Flag<ArticleSummary>(summaryId, reason: "Potential hallucination", ct);
```

## Entity Opt-In

Implement `IReviewable` to mark an entity as reviewable:

```csharp
public class ArticleSummary : Entity<ArticleSummary>, IReviewable
{
    public string Title            { get; set; } = "";
    public string GeneratedSummary { get; set; } = "";
    public SummaryStatus Status    { get; set; } = SummaryStatus.PendingReview;
}
```

## Queue Builder

```csharp
Review.Create<TEntity>()
    .Named(string queueName)
    .Filter(Expression<Func<TEntity, bool>> predicate)   // Which records to show
    .Display(Func<TEntity, object> fields)               // Fields shown to reviewer
    .Approve(Action<TEntity> mutation)                   // What approve does to entity
    .Reject(Action<TEntity> mutation)                    // What reject does
    .Edit(Func<TEntity, object> editableFields)          // Which fields are editable
```

## Actions

| Method | Purpose |
|--------|---------|
| `Review.Approve<T>(id, ct)` | Mark as approved, apply configured mutation |
| `Review.Reject<T>(id, reason, ct)` | Mark as rejected with reason |
| `Review.Edit<T>(id, patch, ct)` | Apply a correction patch |
| `Review.Label<T>(id, labels, ct)` | Attach classification labels |
| `Review.Flag<T>(id, reason, ct)` | Flag for follow-up (not a final action) |

## Feedback Loop

Review events are emitted after each action, enabling downstream consumers (e.g., training dataset pipelines) to capture reviewer decisions:

```csharp
// In AiReviewModule or a background service
services.AddReviewEventHandler<ArticleSummary, SummaryFeedbackHandler>();
```

## Reference

- **Related**: `Koan.AI.Training` (consume feedback as training data), `Koan.AI.Eval` (automated quality gates), `Koan.Data.Core` (entity persistence)
