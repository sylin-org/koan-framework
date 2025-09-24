---
id: WEB-0048
slug: entity-moderation-controller-standard-flow
domain: Web
status: accepted
date: 2025-08-29
title: Entity moderation controller with standard flow, two-generic base, and Before/After hooks
---

## Context

Apps need a drop-in, zero-configuration moderation controller that exposes a consistent set of endpoints and behaviors (draft, submit, withdraw, review queue, approve, reject, return) without per-app overrides. Prior approaches embedded entity-specific side effects inside controller methods, violating separation of concerns, complicating reuse/testing, and making behavior hard to reason about. We want the library to own the generic lifecycle (HTTP, auth, sets, idempotency, transactions), and allow only thin, optional customizations per entity.

Constraints and guardrails:

- Controllers expose HTTP routes via MVC (no inline endpoints); capabilities map to actions centrally.
- Data access must prefer Count/QueryStream/Page over materializing all items; moderation sets are first-class (Draft/Submitted/Approved/Denied).
- Custom behavior should be opt-in, minimal, and testable without MVC.

## Decision

Introduce a two-generic moderation controller orchestrated by a “flow” contract, plus a one-generic convenience type that binds to a standard flow:

- EntityModerationController<TEntity, TFlow> - base controller that owns routing, capability authorization, transitions across moderation sets, transactions, idempotency, and error shaping.
- EntityModerationController<TEntity> : EntityModerationController<TEntity, StandardModerationFlow<TEntity>> - zero-config default with sane, safe behavior and no entity-specific side effects.
- IModerationFlow<TEntity> - a thin hook surface with Before*/After* event hooks per transition and an approval-time transform path.
- IModerationValidator<TEntity> (optional) - guards for ownership and legal transitions; permissive no-op by default.

Instantiation:

- The base creates TFlow via ActivatorUtilities. DI registration is optional; flows can request services if available, otherwise use parameterless constructors. Defaults work with zero configuration.

Hook surface (minimal and focused):

- Flow hooks (all optional; default no-op via default interface methods):
  - BeforeSubmit/AfterSubmitted
  - BeforeWithdraw/AfterWithdrawn
  - BeforeApprove/AfterApproved (BeforeApprove is the place to apply transforms to the submitted snapshot)
  - BeforeReject/AfterRejected
  - BeforeReturn/AfterReturned
- Validator methods (optional):
  - ValidateSubmit/Withdraw/Approve/Reject/Return → Success|Failure(code, reason)

Execution semantics and ordering per action (e.g., Approve):

1. Load state (Current entity from main set; SubmittedSnapshot from Submitted set).
2. Validator.ValidateX(ctx). If fail → 4xx ProblemDetails (422 by default; 409 for concurrency/state conflicts; 403 for capability violations handled by auth).
3. Flow.BeforeX(ctx). Pure, fast, no external I/O; may mutate snapshot via context (e.g., apply transform) or veto with failure.
4. Transaction boundary (library-owned): set moves + main upsert, ordered for idempotent retries.
5. Flow.AfterX(ctx). Post-commit side effects (events/notifications). Failures are logged and do not roll back the transition.

Library responsibilities (non-delegable):

- HTTP routes and payload contracts; capability mapping/attributes; moderation set names; transaction/idempotency; ProblemDetails shaping; pager headers on queue. Stats endpoints are separate from the moderation controller and must use efficient counts/queries.

## Scope

Applies to Koan.Web.Extensions moderation controller(s) and all sample apps adopting moderation flows. Does not change core data adapters or set naming policy. Samples should remove controller-level overrides and move entity-specific behavior into flows/validators when customization is needed.

## Consequences

Benefits:

- Zero-configuration adoption with the one-generic controller; consistent endpoints and responses.
- Strong SoC: library owns lifecycle and persistence; apps express only deltas.
- Reusability and testing: flows/validators are small, unit-testable components; base behavior is tested once.
- Evolvability: additional optional hooks can be added via default interface methods without breaking existing flows.

Trade-offs and risks:

- Generic complexity (two generics) may intimidate; mitigated by the convenience one-generic type.
- Single flow per controller by default (no composition). If composition is needed, a CompositeFlow<TEntity> can be introduced later.
- Semantics must be clear: Before* is pre-commit (no I/O), After* is post-commit (best-effort). Documentation and analyzers can help avoid misuse.
- Flow creation via ActivatorUtilities involves light reflection, which is negligible relative to request cost.

## Implementation notes

Contracts:

- TransitionContext<TEntity> includes: Id, Current (main set), SubmittedSnapshot, User principal, TimeProvider, Options (e.g., ApproveOptions with Transform), CancellationToken, IServiceProvider, and a Mutations helper for safe snapshot updates.
- Approve transform: performed in BeforeApprove. The flow may return a merged snapshot or apply a patch via context; identity is preserved.
- Idempotency: double submit/withdraw should produce 204 NoContent; approving a non-submitted item yields 409/422.
- Authorization: map actions to capabilities centrally (Submit, Withdraw, Queue, Approve, Reject, Return); flows do not gate access.
- Data access: use Count for stats and Page/QueryStream for queues; avoid All-materialization. Queue responses include pager headers (X-Total-Count, X-Page, X-Page-Size, X-Total-Pages).

Out-of-scope for this controller:

- Moderation stats endpoint; provide a separate query controller/service to compute counts efficiently over moderation sets and published items by date.

## Follow-ups

- Add a CompositeFlow<TEntity> to allow ordered composition of multiple flows when a real use case arises.
- Provide a lightweight analyzer or guidance discouraging external I/O in Before\* hooks.
- Document the moderation endpoints and flow contracts under docs/api/web-http-api.md or a dedicated reference page, and update sample apps to the drop-in controller.
- Migrate S7.TechDocs: remove action overrides from its controller; implement a DocumentModerationFlow for status sync (Draft/Review/Published), PublishedAt, and ReviewNotes, or rely on StandardModerationFlow when no deltas are required.

## References

- WEB-0035 - EntityController transformers
- WEB-0046 - Entity capabilities - short endpoints and set routing
- WEB-0047 - Capability authorization - fallback and defaults
- DATA-0061 - Data access semantics (All/Query; streaming; pager)
