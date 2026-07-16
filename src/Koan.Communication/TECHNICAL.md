# Koan Communication technical notes

## Ownership

`Koan.Communication` owns Entity Transport's developer grammar, receiver discovery, snapshot boundary,
context carriage, bounded publication, local dispatch, settlement, and composition facts. Data.Core
contributes only `EntityCardinality`; it has no reference to Communication.

Events will later share only mechanisms whose equivalence is demonstrated. This implementation does
not introduce a generic public pipeline or pre-emptively generalize Transport policy into Data.Core.

## Composition

`KoanCommunicationModule` is discovered through normal `KoanModule` registration. It registers one
immutable `TransportReceiverRegistry`, scoped concrete receiver classes, a singleton coordinator and
ingress runtime, and one host-owned local dispatcher.

`IReceiveEntity` is `[KoanDiscoverable]`. The build registry supplies concrete implementers without an
AppDomain scan. Each closed `IReceiveEntity<TEntity>` implemented by a concrete class is one stable
receiver group. Bindings are validated and ordered once at composition; generic dispatch is closed
once and does not reflect per delivery.

## Publication boundary

The Entity facet normalizes scalar, `IEnumerable<TEntity>`, and `IAsyncEnumerable<TEntity>` sources to
one lazy async source. `TransportCoordinator` then:

1. captures the immutable receiver group set and all composed context carriers once;
2. rejects a missing route before enumerating the source;
3. serializes each yielded Entity with Koan's shared Newtonsoft JSON settings;
4. applies the configured UTF-8 payload bound;
5. writes an immutable envelope into the bounded local channel; and
6. seals a fixed-size acceptance when enumeration completes or fails.

An accepted envelope contains no Entity object reference. It holds JSON, the declared Entity type,
operation/ordinal correlation, the sealed context bag, and the immutable receiver bindings.

## Ingress and dispatch

The in-process runtime has multiple writers and one reader. Its bounded channel uses wait backpressure,
so accepted memory cannot grow through an unbounded queue. For every target group, ingress creates a
fresh DI scope, pushes that scope as the current `AppHost`, restores carriers with `HostTrusted`
provenance, deserializes a fresh Entity, evaluates `Where`, and invokes `Receive`.

Filtering, successful handling, and failure each settle exactly one target counter. Handler exceptions
are logged without payload contents and do not prevent later groups from receiving their copies.
Graceful host stop closes publication, drains accepted envelopes, and then stops. Forced shutdown
depends on cooperative handler cancellation.

## Bounded outcomes

`TransportOperation` holds aggregate counters and one completion source. The public acceptance and
settlement contain no per-item list. Operation state lives only as long as queued/processing envelopes
and caller-held receipts require it.

The local runtime has no retry loop, durable inbox, or dedupe ledger. Each deliberate `Send` uses a new
UUIDv7 operation id. Connector retry identity and receiver-scope deduplication must be proved by the
future adapter conformance suite before any distributed guarantee is published.

## Inspection

The boot module reports adapter, assurance, receiver-group count, capacity, and maximum payload size.
`CommunicationCompositionContributor` records the elected built-in floor, capability tokens, typed
receiver identities, and the number of composed context carriers. These facts use stable constants
and flow to the same operator and agent projections as other Koan composition decisions.

## Unsupported scenarios

- cross-process or restart-surviving delivery;
- broker connector election or logical channel configuration;
- retry, dedupe, dead-letter, replay, or outbox behavior;
- Event occurrence dispatch;
- batch atomicity or transactional coupling to persistence;
- non-Entity payloads;
- shared-reference semantics; or
- non-cooperative receiver shutdown.

Legacy `Koan.Messaging` remains temporarily for existing Jobs wake and Cache coherence bridges. It is a
separate deprecated mechanism and must not be adapted underneath Entity Transport.
