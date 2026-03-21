---
id: AI-0030
slug: AI-0030-review-queues
domain: AI
status: Proposed
date: 2026-03-20
---

# ADR: Review Queues — Human-in-the-Loop Feedback Collection

**Contract**

- **Inputs:** Koan entities (`Entity<T>`) with AI-generated output fields, review queue definitions (filter expression, display projection, action set), reviewer actions (approve, reject, edit, label, flag) submitted via generated REST endpoints, reviewer identity from ambient authentication context.
- **Outputs:** Updated entity state reflecting reviewer decisions (`ReviewStatus`, corrections, labels, flags), domain events per action (`ReviewApproved`, `ReviewRejected`, `ReviewEdited`, `ReviewFlagged`), queue statistics (pending count, approval rate, reviewer throughput), original-vs-corrected pairs available to `Dataset.From<Entity>()` for DPO alignment training (AI-0028).
- **Error Modes:** Queue filter returns zero items: empty list with 200 OK, not an error. Entity modified between queue fetch and action submission: optimistic concurrency via entity version; returns 409 Conflict with current state. Reviewer lacks permission for queue: 403 Forbidden with queue name (no data leak). Action on already-reviewed entity: idempotent — returns current state, does not re-emit domain event. Edit action targets a non-existent field expression: startup validation failure in `AddKoanReview()` with clear diagnostic. Label value outside defined options: 400 Bad Request with allowed values.
- **Acceptance Criteria:** A domain expert (Marta persona) can review pending AI outputs via REST API, approve/reject/edit/label/flag them, and a data scientist (Riku persona) can immediately include those reviewed entities in `Dataset.From<Entity>()` queries — without ETL, without export, without code changes. Corrections produce original+edited pairs usable as DPO training data. Queue statistics are queryable per queue.

**Edge Cases**

- Entity deleted between queue listing and action: Action returns 404 with entity ID. Queue listing on next fetch omits the entity naturally (filter no longer matches).
- Concurrent reviewers act on same entity: First action wins via optimistic concurrency. Second reviewer receives 409 Conflict with the updated state, can re-evaluate and act again.
- Entity does not implement `IReviewable`: Framework stores review state in entity metadata (shadow properties), following the `EmbeddingState` tracking pattern from AI-0020. No schema changes required on the entity class.
- Queue filter matches thousands of items: Pagination is mandatory. Default page size 25, max 100. Queue statistics endpoint returns aggregate counts without loading entities.
- Review action triggers `[Embedding]` re-generation: Expected. An edited `AiResponse` field that participates in `[Embedding]` will re-embed on save, keeping the vector index consistent with the corrected content.
- Multiple queues for the same entity type: Supported. Each queue has independent filters, display projections, and action sets. An entity can appear in multiple queues simultaneously if it matches multiple filters.
- Label field has enum type: Options array must match enum values. Framework validates at startup registration, not at runtime.

## Context

Every ML pipeline has a human feedback bottleneck. The pattern is universal and universally painful: export data to CSV, email it to a domain expert, wait days or weeks, import corrections, reconcile with production data that has since changed. By the time feedback arrives, the data is stale, the model has drifted, and the corrections may no longer apply.

This problem exists because production data and review workflows live in separate systems. The domain expert works in a spreadsheet. The data scientist works in a notebook. The production system serves from a database. Three representations of the same data, synchronized manually.

Koan eliminates this separation. `Entity<T>` **is** the production data. `Dataset.From<Entity>()` (AI-0028) bridges entities to training data without ETL. But `Dataset.From<Entity>()` can only work with what the entity contains. Without a structured review process, the entity contains only two kinds of signal:

1. **Explicit user signals** — ratings, thumbs up/down, written feedback. These require the end user to volunteer effort. Response rates are typically 1-5%.
2. **Implicit behavioral signals** — clicks, dwell time, follow-up questions. These are rich but noisy, and require `Signal.From<T,U>()` (deferred to a future ADR per AI-0022 Part 11).

Neither captures **expert judgment**. The person who truly knows whether an AI-generated support response is correct is not the customer who received it — it is the support team lead who has handled thousands of tickets. The person who knows whether an AI-extracted invoice amount is right is the accountant who processes invoices daily. These domain experts are the missing persona in the AI lifecycle.

### The Missing Persona: Marta (Domain Expert)

Marta is not a developer. She cannot write code, run queries, or use Jupyter notebooks. She knows the domain deeply. She knows whether an AI response is factually correct, appropriately toned, and complete. She knows when an AI classification is wrong and what the correct classification should be.

Today, Marta's expertise is captured through informal channels — Slack messages ("this AI response was wrong"), email threads ("please fix the categorization for these 50 tickets"), or not captured at all. Her corrections never reach the training pipeline because there is no structured path from her judgment to `Dataset.From<Entity>()`.

Review queues give Marta a structured interface: see the AI output alongside relevant context, take an action (approve, reject, correct, label, flag), and move on. Her actions update the entity directly. Riku's next `Dataset.From<SupportTicket>(where: t => t.ReviewStatus == ReviewStatus.Approved)` automatically includes Marta's approved data. No export. No import. No reconciliation.

### Why This Closes the Loop

AI-0022 (Unified AI Lifecycle Vision) defines the closed loop in Part 14:

```
1. Priya builds app with Entity<SupportTicket>     [Client, Chain]
2. Users interact, rate responses                   [entities updated]
3. Marta reviews AI outputs, corrects errors        [Review]        ← THIS ADR
4. Riku trains on reviewed + rated entities          [Dataset, Training]
5. Eval gates ensure quality                         [Eval]
6. Jun deploys to optimal runtime                    [Model]
7. Dana monitors, detects drift, triggers retrain    [Eval, Pipeline]
8. Loop back to step 1                               [Client]
```

Without Review (step 3), the loop has a gap. `Dataset.From<Entity>()` can only filter on fields the application explicitly populates — user ratings, completion flags, resolution timestamps. These are valuable but limited. With Review, every AI output can be systematically evaluated by a domain expert, and the expert's judgment becomes a first-class training signal.

More importantly, **corrections** — edits where Marta fixes an AI response — produce the highest-value training data: DPO (Direct Preference Optimization) alignment pairs. The original AI response is the "rejected" output; Marta's corrected version is the "chosen" output. These pairs directly teach the model what "better" means in the domain expert's judgment, without requiring the data scientist to define reward functions.

### API-Only Surface

This ADR specifies the API surface — queue definition, generated endpoints, domain events, and entity integration. No UI is generated. The review queue is consumed by any HTTP client: a custom frontend, a mobile app, a Retool dashboard, or a CLI tool. A reference sample demonstrating the UI pattern will be created separately.

This is a deliberate architectural choice. Review UIs are domain-specific — a medical record review looks nothing like a support ticket review. Generating opinionated UI would constrain adoption more than it would accelerate it. The API surface is the framework's responsibility; the presentation is the application's responsibility.

## Decision

### Part 1: The `IReviewable` Convention and Shadow State

Entities participating in review queues carry review state. Two approaches are supported.

**Explicit implementation** — the entity declares review properties directly:

```csharp
public interface IReviewable
{
    ReviewStatus ReviewStatus { get; set; }
    string? ReviewedBy { get; set; }
    DateTime? ReviewedAt { get; set; }
    string? RejectionReason { get; set; }
}

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected,
    Edited,
    Flagged
}
```

```csharp
public class SupportTicket : Entity<SupportTicket>, IReviewable
{
    public string Question { get; set; }
    public string? AiResponse { get; set; }
    public string? OriginalAiResponse { get; set; }
    public string? Category { get; set; }
    public int? Quality { get; set; }
    public List<string> Flags { get; set; } = [];

    // IReviewable
    public ReviewStatus ReviewStatus { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}
```

**Convention-based shadow state** — the entity does not implement `IReviewable`, and the framework tracks review state externally. This follows the `EmbeddingState<T>` pattern established in AI-0020, where state is tracked in a companion record keyed by entity ID.

```csharp
// Internal to Koan.AI.Review — not user-facing
internal class ReviewState : Entity<ReviewState>
{
    public Guid TargetEntityId { get; set; }
    public string TargetEntityType { get; set; }
    public ReviewStatus Status { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public Dictionary<string, object?> OriginalValues { get; set; } = new();
    public List<string> Flags { get; set; } = [];
    public Dictionary<string, object?> Labels { get; set; } = new();
}
```

**Resolution order:** If the entity implements `IReviewable`, the framework reads/writes review properties directly on the entity. If not, the framework uses shadow `ReviewState` entities. Queue filter expressions against `ReviewStatus` are rewritten at runtime to join against shadow state when needed.

**Recommendation:** Entities that are central to review workflows should implement `IReviewable` explicitly — it makes `Dataset.From<Entity>()` queries simpler and avoids the join overhead. Shadow state is for cases where adding properties to the entity is undesirable (third-party entity types, minimal schema changes).

### Part 2: Review Actions

Each action is a discrete operation with defined semantics. Actions are **composable** — a queue declares which actions are available, and the generated endpoints reflect only those actions.

```csharp
public static class Review
{
    // ── Queue definition ──

    public static ReviewQueue<T> Create<T>(
        string name,
        Expression<Func<T, bool>> where,
        Expression<Func<T, object>> display,
        ReviewAction<T>[] actions) where T : Entity<T>;

    // ── Action definitions ──

    public static ReviewAction<T> Approve<T>();
    public static ReviewAction<T> Reject<T>(bool requireReason = false);
    public static ReviewAction<T> Edit<T>(Expression<Func<T, object>> field);
    public static ReviewAction<T> Label<T>(
        Expression<Func<T, object>> field,
        object[] options);
    public static ReviewAction<T> Flag<T>(params string[] flagTypes);
}
```

**Approve:**

- Sets `ReviewStatus = ReviewStatus.Approved`.
- Sets `ReviewedBy` to the authenticated user identity.
- Sets `ReviewedAt = DateTime.UtcNow`.
- Saves the entity.
- Emits `ReviewApproved { EntityType, EntityId, Queue, ReviewedBy }`.

**Reject:**

- Sets `ReviewStatus = ReviewStatus.Rejected`.
- Sets `RejectionReason` to the provided reason (required if `requireReason: true`).
- Sets `ReviewedBy` and `ReviewedAt`.
- Saves the entity.
- Emits `ReviewRejected { EntityType, EntityId, Queue, Reason, ReviewedBy }`.

**Edit:**

- Stores the original value before overwriting: captures the pre-edit value in `OriginalAiResponse` (if the entity has a matching `Original{FieldName}` property) or in shadow state `OriginalValues` dictionary.
- Applies the new value to the target field.
- Sets `ReviewStatus = ReviewStatus.Edited`.
- Sets `ReviewedBy` and `ReviewedAt`.
- Saves the entity.
- Emits `ReviewEdited { EntityType, EntityId, Queue, Field, ReviewedBy }`.
- The original + edited pair becomes DPO training data via `Dataset.From<Entity>()` with `DataFormat.Preference`.

**Label:**

- Sets the label field to the provided value (must be one of the defined options).
- Does not change `ReviewStatus` — labeling is orthogonal to approval workflow. An entity can be labeled and then approved in a separate action.
- Saves the entity.
- Emits `ReviewLabeled { EntityType, EntityId, Queue, Field, Value, ReviewedBy }`.

**Flag:**

- Adds the flag type to the entity's `Flags` collection (if the entity has one) or to shadow state.
- Sets `ReviewStatus = ReviewStatus.Flagged`.
- Saves the entity.
- Emits `ReviewFlagged { EntityType, EntityId, Queue, FlagType, ReviewedBy }`.
- Flags are additive — multiple flags can be applied to the same entity.

### Part 3: Queue Registration and Endpoint Generation

Queues are registered at startup via `AddKoanReview()`. The registration validates all expressions at startup — field references, filter expressions, display projections, label options, and flag types are checked before the application starts serving requests.

```csharp
builder.Services.AddKoanReview(review =>
{
    review.Queue<SupportTicket>("ai-response-quality", q => q
        .Where(t => t.AiResponse != null && t.ReviewStatus == ReviewStatus.Pending)
        .Display(t => new { t.Question, t.AiResponse, t.Category, t.CustomerName })
        .Approve()
        .Reject(requireReason: true)
        .Edit(t => t.AiResponse)
        .Label(t => t.Quality, [1, 2, 3, 4, 5])
        .Flag("escalate", "bias", "sensitive"));

    review.Queue<PhotoAsset>("photo-analysis-review", q => q
        .Where(p => p.AiAnalysis != null && p.ReviewStatus == ReviewStatus.Pending)
        .Display(p => new { p.AiAnalysis.Summary, p.AiAnalysis.Tags })
        .Approve()
        .Edit(p => p.AiAnalysis.Summary));
});
```

**Generated endpoints per queue:**

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/review/{queueName}` | List items (paginated, filterable) |
| `GET` | `/api/review/{queueName}/{id}` | Single item with display fields |
| `POST` | `/api/review/{queueName}/{id}/approve` | Approve item |
| `POST` | `/api/review/{queueName}/{id}/reject` | Reject with reason |
| `POST` | `/api/review/{queueName}/{id}/edit` | Edit specific field |
| `POST` | `/api/review/{queueName}/{id}/label` | Apply label/rating |
| `POST` | `/api/review/{queueName}/{id}/flag` | Flag for attention |
| `GET` | `/api/review/{queueName}/stats` | Queue statistics |

Additionally, a discovery endpoint:

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/review` | List all registered queues with metadata (name, entity type, available actions, pending count) |

**Endpoint generation** follows the `EntityController<T>` pattern from `Koan.Web.Controllers` — route registration happens via `MapKoanReview()` in the middleware pipeline:

```csharp
app.MapKoanReview(); // Registers all queue endpoints
```

**Pagination:** List endpoints return paginated results using the standard Koan pagination envelope:

```json
{
  "data": [
    {
      "id": "01965abc-...",
      "display": {
        "question": "How do I reset my password?",
        "aiResponse": "To reset your password, navigate to...",
        "category": "Account Management",
        "customerName": "Acme Corp"
      },
      "reviewStatus": "Pending",
      "createdAt": "2026-03-19T14:30:00Z"
    }
  ],
  "total": 142,
  "page": 1,
  "pageSize": 25
}
```

### Part 4: Queue Statistics

The statistics endpoint provides aggregate metrics without loading individual entities:

```json
{
  "queue": "ai-response-quality",
  "total_pending": 142,
  "total_reviewed": 891,
  "reviewed_today": 23,
  "approved_rate": 0.78,
  "rejected_rate": 0.14,
  "edited_rate": 0.08,
  "average_review_time": "45s",
  "top_rejection_reasons": [
    { "reason": "factually incorrect", "count": 42 },
    { "reason": "too verbose", "count": 31 }
  ],
  "top_flags": [
    { "flag": "bias", "count": 7 },
    { "flag": "sensitive", "count": 3 }
  ],
  "label_distribution": {
    "quality": { "1": 12, "2": 34, "3": 89, "4": 156, "5": 43 }
  },
  "reviewers": [
    { "name": "marta@acme.com", "reviewed": 15, "avg_time": "38s" },
    { "name": "carlos@acme.com", "reviewed": 8, "avg_time": "52s" }
  ]
}
```

Statistics are computed from entity queries and review state, not from a separate analytics store. `average_review_time` is the median time between entity creation (or last status change to `Pending`) and the review action timestamp. `reviewed_today` uses the server's UTC day boundary.

### Part 5: Domain Events

Every review action emits a domain event on the Koan event bus. These events enable downstream workflows without coupling the review system to consumers.

```csharp
public sealed record ReviewApproved(
    string EntityType, Guid EntityId, string Queue, string ReviewedBy);

public sealed record ReviewRejected(
    string EntityType, Guid EntityId, string Queue,
    string Reason, string ReviewedBy);

public sealed record ReviewEdited(
    string EntityType, Guid EntityId, string Queue,
    string Field, string ReviewedBy);

public sealed record ReviewLabeled(
    string EntityType, Guid EntityId, string Queue,
    string Field, object Value, string ReviewedBy);

public sealed record ReviewFlagged(
    string EntityType, Guid EntityId, string Queue,
    string FlagType, string ReviewedBy);
```

**Example downstream handlers:**

- `ReviewFlagged` with `FlagType = "escalate"` triggers a notification to a supervisor.
- `ReviewApproved` on a batch of entities triggers an automatic dataset rebuild.
- `ReviewEdited` increments a counter; when the counter exceeds a threshold, triggers model retraining.

Events are consumed via the standard Koan event handler pattern:

```csharp
public class OnReviewEdited : IDomainEventHandler<ReviewEdited>
{
    public async Task Handle(ReviewEdited @event)
    {
        // Track correction volume; trigger retrain when threshold reached
    }
}
```

### Part 6: Training Data Integration

This is the primary strategic value of review queues. Reviewed entities become training data through `Dataset.From<Entity>()` (AI-0028) without any additional ETL or data transformation.

**Approved responses — supervised fine-tuning data:**

```csharp
var dataset = Dataset.From<SupportTicket>(
    where: t => t.ReviewStatus == ReviewStatus.Approved,
    input: t => t.Question,
    output: t => t.AiResponse);
```

Marta's approval is the quality signal. Only responses that pass domain expert review enter the training set. This is strictly higher quality than filtering on user ratings alone (which suffer from response bias, low response rates, and lack of domain expertise).

**Edited responses — DPO alignment pairs:**

```csharp
var alignment = Dataset.From<SupportTicket>(
    where: t => t.ReviewStatus == ReviewStatus.Edited,
    format: DataFormat.Preference,
    prompt: t => t.Question,
    chosen: t => t.AiResponse,           // Marta's corrected version
    rejected: t => t.OriginalAiResponse); // Original AI output
```

Edits are the highest-value training signal. Each edit encodes a domain expert's judgment about what a better response looks like — specific to a real production input. DPO training on these pairs aligns the model with domain expert preferences without requiring a separate reward model.

**Labeled responses — quality-filtered training:**

```csharp
var highQuality = Dataset.From<SupportTicket>(
    where: t => t.ReviewStatus == ReviewStatus.Approved && t.Quality >= 4,
    input: t => t.Question,
    output: t => t.AiResponse);
```

Labels add granularity beyond binary approve/reject. A response can be approved (correct enough to serve) but rated 3/5 (not exemplary). Training on only 4+ rated responses produces a higher-quality fine-tune.

**Rejected responses — negative examples:**

```csharp
var negatives = Dataset.From<SupportTicket>(
    where: t => t.ReviewStatus == ReviewStatus.Rejected,
    input: t => t.Question,
    output: t => t.AiResponse,
    metadata: t => new { t.RejectionReason });
```

Rejection reasons provide structured information about failure modes. A data scientist can analyze rejection reasons to identify systematic model weaknesses ("factually incorrect" in 30% of rejections suggests a knowledge gap; "too verbose" suggests a generation parameter issue).

### Part 7: Original Value Preservation

When an Edit action is performed, the original AI output must be preserved to create DPO training pairs. Two preservation mechanisms are supported:

**Convention-based property:** If the entity has a property named `Original{FieldName}` (e.g., `OriginalAiResponse` for an edit targeting `AiResponse`), the framework writes the pre-edit value there before applying the correction. This makes the original value a first-class queryable property.

**Shadow state fallback:** If no matching `Original*` property exists, the pre-edit value is stored in the `ReviewState.OriginalValues` dictionary (keyed by field name). `Dataset.From<Entity>()` can access shadow state values via a dedicated accessor.

The convention-based approach is preferred because it keeps training data queries simple:

```csharp
// Convention property: straightforward query
var pairs = Dataset.From<SupportTicket>(
    where: t => t.ReviewStatus == ReviewStatus.Edited,
    prompt: t => t.Question,
    chosen: t => t.AiResponse,
    rejected: t => t.OriginalAiResponse);
```

### Part 8: Authorization

Review queues integrate with the ambient authentication context. The framework does not implement its own authorization system — it defers to the application's existing auth middleware.

**Reviewer identity** is extracted from `HttpContext.User` (or the configured identity provider). The `ReviewedBy` field stores the user identifier (email, username, or claim value — configurable per application).

**Queue-level authorization** can be configured via standard ASP.NET Core authorization policies:

```csharp
review.Queue<SupportTicket>("ai-response-quality", q => q
    .RequirePolicy("ReviewerPolicy")
    // ... actions
);
```

This generates endpoints with `[Authorize(Policy = "ReviewerPolicy")]`. Applications define what "ReviewerPolicy" means — role-based, claim-based, or custom.

### Part 9: Idempotency and Concurrency

**Idempotency:** Performing the same action on an already-reviewed entity is safe. If a reviewer approves an already-approved entity, the endpoint returns 200 with the current state and does not re-emit the domain event. This prevents duplicate events from retry logic or double-clicks.

**Optimistic concurrency:** Actions use the entity's version/concurrency token (standard `Entity<T>` behavior). If two reviewers act on the same entity simultaneously, the first action succeeds and the second receives 409 Conflict. The 409 response includes the updated entity state so the second reviewer can re-evaluate.

**Queue consistency:** The `where` filter is evaluated at query time. Once Marta approves a ticket, it no longer matches `ReviewStatus == ReviewStatus.Pending` and disappears from the queue on the next fetch. No manual "remove from queue" action is needed.

## Consequences

### Positive

- **Closes the feedback loop.** The gap between AI output and training data is eliminated. Domain expert judgment flows directly into `Dataset.From<Entity>()` without ETL, without export, without reconciliation. This is the critical enabler for the closed-loop learning pipeline described in AI-0022.
- **Unlocks DPO alignment from production data.** Edit actions produce original+corrected pairs — the most valuable form of alignment data — from real production inputs evaluated by real domain experts. No synthetic preference generation needed.
- **Elevates the domain expert persona.** Marta is no longer locked out of the AI improvement process. Her expertise is captured structurally and flows into model training automatically.
- **Entity-native — no new data stores.** Review state lives on the entity (via `IReviewable`) or alongside it (via shadow `ReviewState`). No separate review database, no synchronization, no staleness. The reviewed entity IS the training data source.
- **Progressive disclosure.** Simple queues require a filter, a display, and `Approve()`. Advanced queues add edits, labels, flags, and custom authorization. The one-queue hello-world is ~5 lines of registration code.
- **Domain events enable extensibility.** Downstream handlers can trigger notifications, dataset rebuilds, retraining jobs, or analytics pipelines — without the review system knowing about any of them.
- **API-only surface maximizes adoption.** No opinionated UI to fight against. Any frontend technology can consume the endpoints. Reference samples demonstrate the pattern without constraining it.

### Negative / Trade-offs

- **API-only means higher initial effort.** Domain experts cannot use review queues until someone builds a frontend. The tradeoff is deliberate (see Context), but it means the time-to-value includes frontend work. A reference sample mitigates this.
- **Shadow state adds query complexity.** When entities do not implement `IReviewable`, filter expressions must be rewritten to join against `ReviewState`. This is invisible to the user but adds runtime overhead. Recommendation: implement `IReviewable` for entities central to review workflows.
- **Original value preservation has two paths.** The convention-based `Original{FieldName}` property and the shadow `OriginalValues` dictionary create two patterns for the same concern. This is a pragmatic tradeoff — the convention is cleaner but requires schema awareness; the shadow is automatic but harder to query.
- **Statistics computation from entity queries.** No pre-aggregated analytics store. For queues with very high volume (>100K reviewed entities), statistics queries may require indexing on `ReviewStatus`, `ReviewedAt`, and `ReviewedBy`. Standard database indexing guidance applies.
- **No built-in reviewer assignment or routing.** The framework does not assign items to specific reviewers, implement round-robin, or enforce workload balancing. These are application-specific concerns. Domain event handlers can implement assignment logic externally.
- **No offline/batch review.** All actions go through the REST API. Bulk operations (approve all items matching a filter) are not in scope for this ADR. They may be added as a future extension if demand warrants.

## References

- AI-0022: Unified AI Lifecycle Vision — Part 10 (Review context), Part 14 (Closed loop)
- AI-0028: Training and Dataset facades — `Dataset.From<Entity>()`, `DataFormat.Preference` for DPO pairs
- AI-0020: Entity-First AI and Transaction Coordination — `EmbeddingState<T>` pattern (precedent for shadow state tracking), `[Embedding]` lifecycle hooks (re-embed on edit)
- AI-0021: Category-Driven AI with Convention Defaults — `Client.*` surface that generates the AI outputs being reviewed
- `src/Koan.AI/` — Current AI implementation
- `src/Koan.Web.Controllers/` — `EntityController<T>` pattern for generated endpoints
- `src/Koan.Web.Queries/` — `EntityQueryParser` for pagination and filtering
