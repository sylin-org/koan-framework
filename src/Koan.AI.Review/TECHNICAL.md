# Sylin.Koan.AI.Review technical contract

## Activation and queue ownership

Generated module activation registers `ReviewQueueRegistry` and `IReviewActionHandler`. Applications call
`AddKoanReview` only to compile their typed queue definitions. `Review.Create<T>` and `Review.*<T>` are lower-level
factories for the same queue/action records.

## Action behavior

The handler validates entity/reviewer/field inputs and mutates `IReviewable` status, reviewer, timestamp, reason, edit,
label, or optional `List<string> Flags` members through current public properties. Approve/reject/edit/flag update
status; label is additive. Cancellation is accepted for contract consistency but current in-memory mutation has no
awaited external operation.

## Persistence and security boundary

The handler deliberately does not save the Entity. The caller owns authentication/authorization, optimistic
concurrency, persistence, audit/event emission, and exposure through HTTP or UI. Registry state is host configuration,
not a durable review ledger or distributed queue.
