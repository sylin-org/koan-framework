# Koan Communication technical notes

## Ownership

`Koan.Communication` owns two Entity-facing intents—Event occurrence and Entity Transport—and one
internal framework-signal lane. It owns
typed handler discovery, copy boundaries, opaque context carriage, bounded publication, local
dispatch, settlement, and composition facts. Data.Core contributes only `EntityCardinality`; it has
no reference to Communication.

The public lanes remain distinct. Events own occurrence/details/fan-out policy; Transport owns
snapshot/receiver-group policy. Framework signals are typed, bounded module hints with no Entity or
application registration surface. Shared mechanisms do not become a public generic pipeline.

## Composition and provider election

`KoanCommunicationModule` is discovered through normal `KoanModule` registration. One immutable
`CommunicationHandlerCatalog` discovers closed `IHandleEntityEvent<TEntity,TEvent>` subscriptions and
`IReceiveEntity<TEntity>` receivers from the generated registry. Concrete handler classes are scoped;
each dispatch creates a fresh DI and host/context scope.

One host-owned `CommunicationRouter` elects a provider independently for Events, Transport, and
framework signals and
owns their shared wire/dispatch contract. `InProcessCommunicationRuntime` implements the same
`ICommunicationAdapter` seam as external connectors and remains the minimum-priority built-in floor.
Direct application reference provenance, never transitive assembly presence, admits external
candidates. Explicit provider options override direct intent; semantic capabilities, assurance,
Core-owned provider priority, and stable ID resolve eligible candidates.

The local adapter owns one bounded channel and worker per declared lane. The lanes share
lifecycle and aggregate operation accounting but cannot head-of-line block one another. Generic
dispatch is closed during catalog construction rather than reflected per item. A directly intended
provider that cannot start fails the host; there is no local reach fallback.

## Publication boundary

The Entity facets normalize scalar, `IEnumerable<TEntity>`, and `IAsyncEnumerable<TEntity>` sources
to one lazy async source. Each coordinator then:

1. resolves the lane route and captures all composed context carriers once;
2. validates policy knowable at the selected boundary before enumeration;
3. serializes each yielded Entity, plus Event details when present, with shared JSON settings;
4. applies the configured combined UTF-8 payload bound;
5. publishes an immutable host wire envelope through the elected adapter; and
6. seals a fixed-size acceptance when enumeration completes or fails.

Every accepted envelope contains JSON rather than an object reference. Correlation includes the
operation and source ordinal; Events additionally assign one occurrence id and timestamp per Entity.
Every Event subscription for that Entity sees the same occurrence identity.

## Ingress and dispatch

The local lanes are multiple-writer/single-reader and use wait backpressure. Framework-signal callers
use a separate bounded, non-blocking egress because loss only delays the owning subsystem's fallback.
External adapters bind
the same stable target declarations and return host-owned wire bytes to the same ingress. For every
target group, ingress creates a scope, pushes it as the current `AppHost`, restores carriers using the
adapter's declared trust provenance, deserializes fresh Entity state and Event details, evaluates
`Where`, and invokes the typed handler.

Filtering, successful handling, and failure each settle one target counter. Handler exceptions are
logged without payload contents and do not prevent later groups from receiving their copies. Graceful
host stop closes publication, drains all lanes, and then stops. Forced shutdown depends on
cooperative handler cancellation.

## Lane policy

An Event kind may be raised without details unless decorated with `[EventDetailsRequired]`; that
misuse is rejected before source enumeration. Zero Event subscriptions is a valid zero-target
occurrence. Every deliberate Raise is a new operation, and every accepted Entity is a new occurrence.

Transport rejects a known zero receiver-group set before source enumeration. An external mandatory
route can learn that no group exists only when publication is returned; the typed failure then carries
the accepted/rejected prefix. Every deliberate Send is a new operation and distributes the accepted
Entity snapshot to each receiver group.

The public acceptance and settlement types contain aggregate counters, not per-item collections.
Operation state lives only while queued/processing envelopes and caller-held receipts require it.

Framework signals have no public receipt. `TryPublish` reports only admission to the bounded host
egress; provider failures are observable through provider health and facts. Jobs wake is the first
consumer: its signal carries no work or business context, replicas compete in one stable worker group,
and the ledger poll remains the correctness fallback.

## Inspection

The boot module and `CommunicationCompositionContributor` report each lane's elected provider,
reason, priority, assurance, settlement observability, handler-group counts and identities, local
bounds where applicable, payload limits, and composed context-carrier count. Stable constants feed
the same startup, operator, and authorized-agent fact projections.

## Unsupported scenarios

- provider-specific features outside an adapter's declared lane and assurance;
- logical channel authoring;
- retry, dedupe, dead-letter, replay, or outbox behavior;
- batch atomicity or transactional coupling to persistence;
- application-authored non-Entity payloads (typed framework signals remain internal);
- shared-reference semantics; or
- non-cooperative handler shutdown.

Legacy `Koan.Messaging` remains temporarily for Cache coherence and other previous-generation consumers. It is
a separate deprecated mechanism and must not be adapted underneath Entity Communication.
