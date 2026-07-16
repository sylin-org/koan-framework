# Koan Communication technical notes

## Ownership

`Koan.Communication` owns two Entity-facing intents—Event occurrence and Entity Transport—and two
internal framework routes: stable competing groups and every-active-node broadcast. It owns
typed handler discovery, copy boundaries, opaque context carriage, bounded publication, local
dispatch, settlement, and composition facts. Data.Core contributes only `EntityCardinality`; it has
no reference to Communication.

The public lanes remain distinct. Events own occurrence/details/fan-out policy; Transport owns
snapshot/receiver-group policy. Framework signals are typed, bounded module hints with no Entity or
application registration surface. Cache owns invalidation meaning; Communication only carries its
internal node broadcast. Shared mechanisms do not become a public generic pipeline.

## Composition and provider election

`KoanCommunicationModule` is discovered through normal `KoanModule` registration. One immutable
`CommunicationHandlerCatalog` discovers closed `IHandleEntityEvent<TEntity,TEvent>` subscriptions and
`IReceiveEntity<TEntity>` receivers from the generated registry. Concrete handler classes are scoped;
each dispatch creates a fresh DI and host/context scope.

One host-owned `CommunicationRouter` builds the complete immutable route plan. It normalizes the
inferred `default` plus startup-declared business channels, elects a provider independently for each
public lane/channel and each internal route, creates channel-qualified bindings, scopes each adapter
host to only its elected bindings, and owns the shared wire/dispatch contract.
`InProcessCommunicationRuntime` implements the same
`ICommunicationAdapter` seam as external connectors and remains the minimum-priority built-in floor.
Direct application reference provenance admits external candidates. A layered candidate may also participate when
its owning engine activates it; declaring zero lanes keeps it dormant. Explicit provider options override direct intent; semantic capabilities, assurance,
Core-owned provider priority, and stable ID resolve eligible candidates.
The adapter descriptor is also the startup source for settlement observability; each publication
acceptance must match it, so receipts and composition facts cannot tell different stories.

The local adapter owns one bounded queue and worker per semantic lane. Business channels that elect
the local adapter share their lane's bound; distinct lanes cannot head-of-line block one another. The
lanes share lifecycle and aggregate operation accounting. Generic
dispatch is closed during catalog construction rather than reflected per item. A directly intended
provider that cannot start fails the host; there is no local reach fallback.

Named channels are declared at `Koan:Communication:Channels:{name}` and may pin Transport and Events
independently. A missing pin follows normal direct-reference/built-in election. Every public typed
handler group binds once to every declared public channel; internal Jobs/Cache routes remain on their
framework-owned default channels. Channel identity is part of binding and wire validation. Unknown,
malformed, duplicate-normalized, and reserved-`default` declarations fail before publication; an
unknown terminal channel fails before source enumeration.

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

Framework routes have no public receipt. `TryPublish`/`TryBroadcast` report only admission to the bounded host
egress; provider failures are observable through provider health and facts. Jobs wake is the first
consumer: its signal carries no work or business context, replicas compete in one stable worker group,
and the ledger poll remains the correctness fallback. Cache broadcasts use a unique node-scoped binding;
receivers filter their own origin, evict only L1, and rely on L1 TTL as the loss bound.

## Inspection

The boot module and `CommunicationCompositionContributor` report each lane/channel's elected provider,
reason, priority, assurance, settlement observability, handler-group bindings, applicable local
bounds, payload limits, and composed context-carrier count. Stable constants feed the same startup,
operator, and authorized-agent fact projections.

## Unsupported scenarios

- provider-specific features outside an adapter's declared lane and assurance;
- dynamic channel creation, automatic branching, mirroring, or failover;
- channel-based authorization, confidentiality, or receiver selection;
- retry, dedupe, dead-letter, replay, or outbox behavior;
- batch atomicity or transactional coupling to persistence;
- application-authored non-Entity payloads (typed framework signals remain internal);
- shared-reference semantics; or
- non-cooperative handler shutdown.

Legacy `Koan.Messaging` is a separate previous-generation mechanism and is not adapted underneath Entity Communication.
