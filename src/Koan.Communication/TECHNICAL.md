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

`KoanCommunicationModule` contributes construction-free descriptor availability. On the supported
composition path, the host constitution activates it only from direct or explicit bundle intent, then
one retained module instance performs registration and startup; the constitution projects the
activation fact. No parallel initializer, registrar, DI-construction, or provenance-reconstruction
path owns its lifecycle. A host without a generated constitution may use Core's explicitly degraded
fallback; that mode is not
declaration evidence. One immutable
`CommunicationHandlerCatalog` discovers closed `IHandleEntityEvent<TEntity,TEvent>` subscriptions and
`IReceiveEntity<TEntity>` receivers from the generated registry. Concrete handler classes are scoped;
each dispatch creates a fresh DI and host/context scope.

One host-owned `CommunicationRouter` builds the complete immutable route plan. It normalizes the
inferred `default` plus startup-declared business channels, elects a provider independently for each
public lane/channel and each internal route, creates channel-qualified bindings, scopes each adapter
host to only its elected bindings, and owns the shared wire/dispatch contract.
The router compiles adapters through Core's immutable provider catalog for ID/alias validation,
direct-reference matching, memoized priority, and stable ties. Communication still owns lane eligibility,
assurance ordering, layered/built-in admission, failure policy, binding construction, and startup. Each
route carries one safe selection receipt, and composition facts project that receipt rather than re-electing.
The adapter descriptor also declares immutable context-ingress provenance. Public Transport and Events
routes are eligible only when that provenance meets every composed carrier requirement; unknown trust
values and explicitly selected under-trusted providers fail before adapter startup. Framework signals
and broadcasts are context-free and therefore retain the unverified floor.
`InProcessCommunicationRuntime` implements the same
`ICommunicationAdapter` seam as external connectors and remains the minimum-priority built-in floor.
Direct application reference provenance admits external candidates. A layered candidate may also participate when
its owning engine activates it; declaring zero lanes keeps it dormant. Explicit provider options override direct intent; semantic capabilities, assurance,
Core-owned provider priority, and stable ID resolve eligible candidates.
The adapter descriptor is also the startup source for settlement observability; each publication
acceptance must match it, so receipts and composition facts cannot tell different stories.

The local adapter owns one backlog and one sequential consumer per binding: every receiver,
subscription, and framework group on every channel it carries. No binding can head-of-line block
another, and none can block a publisher. Bindings share lifecycle and aggregate operation accounting.
Generic dispatch is closed during catalog construction rather than reflected per item. A directly
intended provider that cannot start fails the host; there is no local reach fallback.

Named channels are declared at `Koan:Communication:Channels:{name}` and may pin Transport and Events
independently. A missing pin follows normal direct-reference/built-in election. Every public typed
handler group binds once to every declared public channel; internal Jobs/Cache routes remain on their
framework-owned default channels. Channel identity is part of binding and wire validation. Unknown,
malformed, duplicate-normalized, and reserved-`default` declarations fail before publication; an
unknown terminal channel fails before source enumeration.

## Publication boundary

The Entity facets normalize scalar, `IEnumerable<TEntity>`, and `IAsyncEnumerable<TEntity>` sources
to one lazy async source. Each coordinator then:

1. resolves the lane route, binds every hard segmentation obligation, and captures the composed context once;
2. validates policy knowable at the selected boundary before enumeration;
3. serializes each yielded Entity, plus Event details when present, with shared JSON settings;
4. encodes the immutable host wire envelope and, when the elected adapter declares a transport limit
   (`CommunicationAdapterDescriptor.MaxPayloadBytes`), rejects a larger encoded publication with
   `PayloadTooLarge`; the process-local adapter declares none;
5. publishes the envelope through the elected adapter; and
6. seals a fixed-size acceptance when enumeration completes or fails.

Every accepted envelope contains JSON rather than an object reference. Correlation includes the
operation and source ordinal; Events additionally assign one occurrence id and timestamp per Entity.
Every Event subscription for that Entity sees the same occurrence identity.

## Ingress and dispatch

Local publication never waits. For each target binding the adapter offers the delivery to that
binding's backlog, which is bounded by memory, never by a count. The backlog counts the encoded bytes
of every publication waiting in it; one encoded publication is shared by every binding it targets and
counts once in each. A delivery is admitted while the waiting bytes are below the budget, so a binding
with nothing waiting always admits, and a binding's memory exceeds `InProcessBacklogBytes` by at most
one publication, plus per-delivery bookkeeping and the one delivery in flight, which are not counted;
shared publications make the process total usually less than the sum. A handler whose backlog stays
within the budget never loses a delivery. A binding whose waiting publications reach it fails further
deliveries immediately and visibly, settling each as failed and overflowed.
Admission and the overflow episode are decided under one lock, so each episode logs one warning when
it begins and one note, with its true count, when the binding admits again. The budget reaches only
the binding that is over it. Each binding's consumer takes one delivery at a time, in the order it was
offered, so per-binding order is publication order; there is no order across bindings. It runs each
handler on the thread pool rather than its own thread and waits for it there, so a forced stop always
reaches that wait. Framework-signal callers use a separate bounded, non-blocking egress
because loss only delays the owning subsystem's fallback; that egress holds a fixed number of small
fixed-shape hints and drops the newest when full.

External adapters bind the same stable target declarations and return host-owned wire bytes to the
same ingress. For every target group, ingress creates a scope, pushes it as the current `AppHost`,
restores carriers using the adapter's declared trust provenance, deserializes fresh Entity state and
Event details, evaluates `Where`, and invokes the typed handler under `HandlerTimeout`: the handler's
token is cancelled when the timeout passes, and a handler that ends because of it is a failed delivery.
Dispatch returns when the handler returns, so an adapter that awaits it before taking a binding's next
delivery keeps a handler that ignores cancellation from holding anything but its own binding.

`CommunicationContextPlan` is the pillar-owned wrapper over Core's memoized
`SegmentationContextPlan`. It requires every applicable hard axis before restore and re-binds the
segmentation scope under the restored carrier before filters or handlers. A deterministic Development
fallback is materialized into the outgoing opaque bag only for a hard typed operation; it is not ambient
global state and cannot be re-resolved differently by a peer.

Filtering, successful handling, failure, and overflow each settle one target counter; overflow also
counts as failure. Handler exceptions are logged without payload contents and never reach another
group. Graceful host stop closes publication, drains every binding, and then stops. Forced shutdown
cancels running handlers and fails what remains queued; a handler that ignores cancellation, or blocks
its thread, is abandoned with its delivery failed, so stop is not held by it.

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

The retained boot module and its `CommunicationCompositionFacts` projector report each lane/channel's elected provider,
reason, priority, assurance, settlement observability, handler-group bindings, applicable local
bounds, payload limits, and composed context-carrier count. Stable constants feed the same startup,
operator, and authorized-agent fact projections.

When hard segmentation is active, the same fact envelope reports the Communication realization and a
per-route guarantee statement. Logical context is `enforced-or-rejected`; typed contract/group routing
is named separately; local/RabbitMQ physical topology remains shared; RabbitMQ does not imply payload
confidentiality; and remote settlement remains unavailable unless an adapter explicitly proves it.

## In a scope

A `Raise` or `Send` inside `EntityContext.Transaction` or `EntityContext.Hold` publishes nothing. It reads its source and copies it, inside the scope, and
`ITransactionCoordinator.AfterCommit` publishes the copies once the scope has committed (ARCH-0142). A rollback publishes nothing, the acceptance says `AwaitsCommit`, and a failure of
the publication is logged, because the commit stands. A process that ends between the commit and the publication loses it.

## Unsupported scenarios

- provider-specific features outside an adapter's declared lane and assurance;
- dynamic channel creation, automatic branching, mirroring, or failover;
- channel-based authorization, confidentiality, or receiver selection;
- process-local retry, dedupe, dead-letter, replay, or outbox behavior;
- batch atomicity, or an outbox that survives the process between a commit and its publication;
- application-authored non-Entity payloads (typed framework signals remain internal);
- shared-reference semantics; or
- non-cooperative handler shutdown.

No arbitrary-object message bus is adapted underneath Entity Communication.
