---
type: SPEC
domain: framework
title: "R07-08 - Faithful Local Entity Events"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-15
  status: passed
  scope: foundation AddKoan process-local Events, typed subscriptions, occurrence identity, details, context, acceptance, settlement, and agent guidance
---

# R07-08 — Faithful local Entity Events

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-07
- Unlocks: communication mesh and broker parity
- Owner: Communication Events grammar, local fan-out, occurrence policy, receipts, and facts

## Meaningful outcome

A foundation application using only `AddKoan()` can state that a typed business occurrence happened
to one Entity, a finite selection, or a lazy stream. Koan discovers business-named subscriptions,
fans out isolated snapshots under the captured context, and reports acceptance and settlement without
requiring a bus, registration DSL, adapter, or routing configuration:

```csharp
public sealed record OrderApproved;

public sealed class RecordApproval : IHandleEntityEvent<Order, OrderApproved>
{
    public Task Handle(
        Order order,
        EventOccurrence<OrderApproved> occurrence,
        CancellationToken ct) => Record(order, ct);
}

var accepted = await order.Events.Raise<OrderApproved>(ct);
var settled = await accepted.WaitForSettlement(ct);
```

The source Entity already supplies identity and snapshot, so the common path creates no empty event
object. A details-required occurrence uses an explicit details value and fails before source
enumeration when omitted.

## Architecture

- `Koan.Communication` remains one pillar and one package. Applications see the distinct `Events` and
  `Transport` Entity nouns; no public bus or generic pipeline is introduced.
- Communication contributes both facets from the normal Entity namespace. Referencing the module is
  sufficient for IntelliSense discovery; removing it removes both facets at compile time.
- `IHandleEntityEvent<TEntity, TEvent>` is the sole application subscription path. Generated Koan
  discovery finds business-named implementations and DI creates each handler in a fresh scope.
- Events and Transport share only a host-owned bounded runtime, ingress scope/context restoration,
  aggregate operation accounting, handler catalog, and lifecycle. Their public contracts,
  coordinators, envelopes, target bindings, identities, failures, acceptance, and settlement remain
  lane-owned.
- Every deliberate `Raise` creates a new occurrence. Every logical subscription receives the same
  occurrence identity with fresh deserialized Entity and details objects. Zero subscriptions are a
  valid zero-target occurrence.
- Awaiting `Raise` means bounded publication acceptance, not handler completion. Operation-scoped
  settlement and graceful host drain remain separate.

## Principal supersession

This slice does not restore persistence `Entity.Events`, adapt arbitrary-object Messaging, or reuse
`services.On<T>()`. Persistence behavior remains `Entity.Lifecycle`; legacy Messaging remains only for
the unmigrated Jobs wake and Cache coherence bridges.

## Evidence

- Entity language passes 20/20. The compile contract covers scalar/set/stream Raise, typed handlers,
  explicit details, normal Entity-namespace discovery, Communication removal, invalid receivers, and
  all-module coexistence with Transport.
- Communication passes 28/28 through a real foundation `AddKoan()` host. The Event cases prove
  occurrence identity, repeated raises, fan-out, per-subscription Entity/details copies, zero
  subscriptions, details-required pre-enumeration rejection, optional details, filtering, failure,
  acceptance-before-handler, context capture/absence suppression, bounded partial cancellation,
  combined payload bounds, graceful drain, repeated hosts, facts, and independent Event/Transport
  lanes. All existing Transport cases remain green after runtime coalescence.
- `Koan.Communication` builds warning-as-error with zero warnings/errors. Direct Communication and
  foundation packs succeed. Changed examples pass 4/4 after the shared example compiler gained the
  foundation Communication reference. Docs lint reports 0 errors and 1,572 historical warnings;
  skills lint reports 0 errors/warnings.
- Release certification is intentionally excluded from this ordinary implementation loop.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-15
- Unsupported scenarios: distributed delivery, retries/dedupe, connector/channel election, RabbitMQ,
  persistence transaction coupling, non-Entity payloads, collection atomicity, and non-cooperative
  handler shutdown.
- Follow-up: the next bounded slice explores the manifest/channel mesh and external-adapter parity;
  no broker API is implied by this local closure.
- Reviewer: Codex implementation and executable evidence under maintainer approval.

## Honest boundaries

- Process-local, memory-only, and not restart durable.
- No retries, dedupe ledger, dead letter, replay, outbox, event sourcing, or persistence transaction
  coupling.
- No connector/channel election, RabbitMQ, cross-application contract, or Jobs/Cache bridge migration.
- Details-required misuse fails before source enumeration; build-time analyzer enforcement is deferred
  rather than introducing a second analyzer package in this slice.
- No collection atomicity and no guarantee that application handler side effects are idempotent.

## Stop conditions

- Stop any design that makes an Event a renamed Transport snapshot or persistence hook.
- Stop any Event implementation that requires a listener for the occurrence to be valid.
- Stop any shared kernel that erases lane-specific identity, fan-out, failure, or receipt semantics.
- Stop any Communication code that names tenant, subject, or another context axis.
- Stop any Data dependency on Communication.
- Stop before broker breadth, publication, push, tag, release, or private downstream inspection.
