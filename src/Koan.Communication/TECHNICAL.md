# Koan Communication technical notes

## Ownership

`Koan.Communication` owns two Entity-facing intents: Event occurrence and Entity Transport. It owns
typed handler discovery, copy boundaries, opaque context carriage, bounded publication, local
dispatch, settlement, and composition facts. Data.Core contributes only `EntityCardinality`; it has
no reference to Communication.

The public lanes remain distinct. Events own occurrence/details/fan-out policy; Transport owns
snapshot/receiver-group policy. Shared mechanisms do not become a public generic pipeline.

## Composition

`KoanCommunicationModule` is discovered through normal `KoanModule` registration. One immutable
`CommunicationHandlerCatalog` discovers closed `IHandleEntityEvent<TEntity,TEvent>` subscriptions and
`IReceiveEntity<TEntity>` receivers from the generated registry. Concrete handler classes are scoped;
each dispatch creates a fresh DI and host/context scope.

One host-owned `InProcessCommunicationRuntime` owns separate bounded Event and Transport channels and
workers. The lanes share lifecycle and aggregate operation accounting but cannot head-of-line block
one another. Generic dispatch is closed during catalog construction rather than reflected per item.

## Publication boundary

The Entity facets normalize scalar, `IEnumerable<TEntity>`, and `IAsyncEnumerable<TEntity>` sources
to one lazy async source. Each coordinator then:

1. captures its immutable typed target set and all composed context carriers once;
2. validates lane policy before enumeration (missing Transport receivers or required Event details);
3. serializes each yielded Entity, plus Event details when present, with shared JSON settings;
4. applies the configured combined UTF-8 payload bound;
5. writes an immutable lane envelope into its bounded channel; and
6. seals a fixed-size acceptance when enumeration completes or fails.

Every accepted envelope contains JSON rather than an Entity reference. Correlation includes the
operation and source ordinal; Events additionally assign one occurrence id and timestamp per Entity.
Every Event subscription for that Entity sees the same occurrence identity.

## Ingress and dispatch

Each lane is multiple-writer/single-reader and uses wait backpressure. For every target group, ingress
creates a scope, pushes it as the current `AppHost`, restores carriers with `HostTrusted` provenance,
deserializes fresh Entity state and Event details, evaluates `Where`, and invokes the typed handler.

Filtering, successful handling, and failure each settle one target counter. Handler exceptions are
logged without payload contents and do not prevent later groups from receiving their copies. Graceful
host stop closes publication, drains both lanes, and then stops. Forced shutdown depends on
cooperative handler cancellation.

## Lane policy

An Event kind may be raised without details unless decorated with `[EventDetailsRequired]`; that
misuse is rejected before source enumeration. Zero Event subscriptions is a valid zero-target
occurrence. Every deliberate Raise is a new operation, and every accepted Entity is a new occurrence.

Transport rejects zero receiver groups before source enumeration. Every deliberate Send is a new
operation and distributes the accepted Entity snapshot to each receiver group.

The public acceptance and settlement types contain aggregate counters, not per-item collections.
Operation state lives only while queued/processing envelopes and caller-held receipts require it.

## Inspection

The boot module and `CommunicationCompositionContributor` report both local adapters, assurance,
handler-group counts and identities, per-lane capacity, payload limits, and composed context-carrier
count. Stable constants feed the same startup, operator, and authorized-agent fact projections.

## Unsupported scenarios

- cross-process or restart-surviving delivery;
- connector election or logical channel configuration;
- retry, dedupe, dead-letter, replay, or outbox behavior;
- batch atomicity or transactional coupling to persistence;
- non-Entity payloads;
- shared-reference semantics; or
- non-cooperative handler shutdown.

Legacy `Koan.Messaging` remains temporarily for existing Jobs wake and Cache coherence bridges. It is
a separate deprecated mechanism and must not be adapted underneath Entity Communication.
