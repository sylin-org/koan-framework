---
type: SPEC
domain: framework
title: "R07-07 - Faithful Local Entity Transport"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-15
  status: passed
  scope: foundation AddKoan process-local Transport, typed receivers, context, acceptance, settlement, package lineage, and public guidance
---

# R07-07 — Faithful local Entity Transport

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-06
- Unlocks: Event occurrence policy on the same Communication boundary
- Owner: Communication grammar, local routing, ingress, receipts, and facts

## Meaningful outcome

A foundation application already using `AddKoan()` can move the exact Entity snapshot it holds to
business-named local receivers without registration, adapter code, configuration, or Data coupling:

```csharp
public sealed class ImportOrder : IReceiveEntity<Order>
{
    public bool Where(Order order) => order.Ready;
    public Task Receive(Order order, CancellationToken ct) => Import(order, ct);
}

var accepted = await order.Transport.Send(ct);
var settled = await accepted.WaitForSettlement(ct);
```

The same terminal works pointwise for a finite selection and a lazy async stream. `Send` means bounded
publication acceptance. Every receiver group gets a fresh deserialized copy under the context captured
at terminal invocation; correlated local settlement remains a separate observation.

## Architecture

- One new `Koan.Communication` package owns the public Transport grammar and built-in local floor. No
  Events/Transport/Abstractions/InMemory package fragments are introduced.
- `IReceiveEntity<TEntity>` is the sole application receiver path. Generated Koan discovery finds
  business-named implementations and DI creates each receiver in a fresh scope.
- Data.Core contributes only `EntityCardinality`. It has no Communication dependency and knows nothing
  about dispatch, context, receipts, or routing.
- Publication checks the immutable route before source enumeration, captures Core context carriers
  once, serializes each Entity with Koan JSON, enforces a payload bound, and writes to a bounded
  in-process channel.
- One deterministic local worker gives every receiver group a separately deserialized snapshot. Its
  ingress pushes the correct `AppHost`, restores or suppresses every context axis with host trust,
  evaluates typed `Where`, and invokes `Receive`.
- `TransportAcceptance` and `TransportSettlement` are fixed-size aggregates. Publication cancellation
  and failure preserve the accepted prefix; handler outcomes never rewrite acceptance.
- Graceful host stop closes publication and drains accepted work. Handlers must honor cancellation.
- Startup provenance and composition facts report the local adapter, process-memory assurance, bounds,
  receiver groups, capability tokens, and context carriage.

## Principal supersession

This is a new semantic implementation, not an adapter over legacy Messaging. Broad arbitrary-class
`Send`, `services.On`, proxy buffering, mutable-reference InMemory delivery, and provider-specific
cardinality are not reused or aliased.

Legacy Messaging remains only because Jobs wake and Cache coherence still depend on its internal
bridges. Their migration and deletion belong to the later mesh/internal-convergence slice.

## Evidence

- Entity-language compile contract passes focused 12/12 and full 16/16, including scalar/set/stream
  Transport, typed receivers, module removal, non-Entity rejection, and coexistence with current facets.
- Communication passes 14/14 through a real foundation `AddKoan()` host: acceptance-before-handler,
  sender/group copy isolation, order/multiplicity, new send identities, typed filters, handler failure,
  zero-receiver pre-enumeration failure, tenant capture/absence suppression, bounded partial
  cancellation, payload rejection, boot facts, graceful drain, repeated hosts, and startup validation.
- Packaging passes 54/54 after foundation admission and automatic source-app lockfile regeneration.
- `Koan.Communication` builds with warnings-as-errors: zero warnings and zero errors.
- Direct Communication and foundation bundle packs succeed. The foundation nuspec carries
  `Sylin.Koan.Communication` 0.18 and the direct package contains its DLL, XML API docs, and README.
- Changed examples pass 2/2. Docs/TOC lint reports 0 errors and 1,572 historical warnings.
- FirstUse and GoldenJourney lockfiles record `Koan.Communication` 0.18; the post-commit lock comparison
  is the final deterministic repository-state check.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-15
- Unsupported scenarios: distributed delivery, retries/dedupe, Events, connector/channel election,
  non-Entity payloads, collection atomicity, and non-cooperative receiver shutdown.
- Follow-up: R07-08 adds Event occurrence semantics over only the mechanisms this slice proved shared.
- Reviewer: Codex implementation and executable evidence under maintainer approval.

## Honest boundaries

- Process-local, memory-only, no restart durability.
- One deterministic worker; no published throughput or parallelism claim.
- No retry loop, dedupe ledger, dead letter, replay, or outbox. Connector retry identity and dedupe
  remain conformance requirements, not inferred guarantees.
- No Events, broker connector, channel election, RabbitMQ, cross-application contract, or internal
  Jobs/Cache migration.
- No batch atomicity and no persistence coupling.
- Non-cooperative receiver shutdown is unsupported.

## Stop conditions

- Stop any implementation that sends the sender's mutable Entity reference.
- Stop any Transport code that names tenant, subject, or another context axis.
- Stop any Data dependency on Communication.
- Stop any generic pipeline or public infrastructure registration path.
- Stop before publication, push, tag, release, or private downstream inspection.
