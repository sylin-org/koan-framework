# Sylin.Koan.Data.Cutover technical contract

## Responsibility

The package owns planning, bounded copy, canonical verification, and activation for one graduated default-route
transition. SQLite, MongoDB, and PostgreSQL form the first proven provider set. It consumes three host-owned Data Core
parts and adds one leaf operation package:

- `DefaultDataRouteAuthority` owns durable active-route truth, per-route content generations, and quarantine state.
- `DataOperationHorizon` owns mutation admission and draining around complete semantic operations.
- `DataApplicationManifest` owns the exhaustive discovered Entity-root inventory and eligibility blockers.
- `DefaultRouteTransitionService` owns the migration protocol and public receipts.

Physical `DataSourcePlan` objects and the source/provider registries remain immutable. Cutover changes one operational
pointer; it does not rewrite configuration or adapter selection.

## Planning envelope

Each `Plan()` recomputes the active and target plans, provider capability, physical identities, storage status, complete
container inventories, root storage mappings, bounded-traversal support, and manifest blockers. Inspection is
non-creating. A missing managed SQLite file or reachable empty MongoDB/PostgreSQL database is eligible; an unavailable,
corrupt, locked, nonempty, aliased, read-only, external, or same-connection target is not. An incomplete provider-limited
container inventory rejects rather than being treated as complete.

All concrete `IEntity` implementations are source-generated discoveries. Family variants collapse to one root and one
physical container. Explicit adapter-pinned and Database-axis roots stay outside the default route. Any unexplained
physical source container rejects the operation so the protocol cannot silently omit stored application data.

## Mutation and verification order

`Run()` performs these boundaries in order:

1. Plan outside the serialized boundary.
2. Acquire the authority's single-host transition lease and plan again.
3. Close source writes and target reads/writes; drain admitted mutations.
4. Inspect a third time under the closed barriers.
5. Persist pending intent, then persist `TargetMayContainData` before the first target mutation.
6. For each eligible root, provision the target container, read provider-bounded source pages in Entity-ID order, and
   upsert exact bounded batches.
7. Reread the source, fetch the corresponding target records by exact ID in bounded batches, and compare ID, runtime
   family identity, and canonical record bytes; independently prove equal target cardinality.
8. Persist the new route revision and fresh target content generation with write-through replacement, publish the
   immutable in-memory snapshot, and reopen admission.

Canonical records include a format tag, root identity, runtime family identity, deterministically ordered JSON object
members, explicit type tags, invariant scalar/date/binary encodings, and length framing. Receipts and runtime facts
contain only safe identities, counts, hashes, durations, and correction codes.

Caller cancellation is honored through verification. Durable commit deliberately ignores caller cancellation once it
begins so the caller cannot receive an ambiguous activation result. Any pre-commit failure keeps the old route active;
if mutation might have occurred, disposal persists target quarantine.

## Coherence outside the operation package

Data Core binds every resolved repository or Direct operation to an immutable route generation. Default-derived
handles additionally bind the authority revision. Repository cache identity includes binding origin; Entity cache keys
receive the physical route namespace explicitly through the compatible route-aware decorator context. Deferred
transactions, transfers, Direct transactions, patch, and predicate-delete hold mutation horizons around their complete
multi-step semantics.

Reads admitted before activation may finish on the old route. A later operation through a retained default-derived
facade fails with `StaleDataRouteException`; streams therefore cannot mix generations between pages. Explicit handles
to the retained old configured source remain valid at its unchanged physical content generation.

## Deliberate exclusions

This package does not provide rolling multi-host cutover, external-writer fencing, incremental replication, merge,
schema transformation, ungraduated-provider transfer, rollback, reverse synchronization, deletion of the old database,
or migration of segmented/managed/transformed/mapped physical slices. Provider-handled ID order is used only for
bounded traversal; target verification is identity-matched because cross-provider string collation is not a Koan
guarantee. Excluded semantics require new architecture envelopes rather than flags on this one.
