title: Sylin.Koan.Data.Vector.Connector.InMemory - Technical Reference
description: Bounded exact automatic-floor Vector provider for Koan.
packages: [Sylin.Koan.Data.Vector.Connector.InMemory]
source: src/Connectors/Data/Vector/InMemory/

## Composition

`InMemoryVectorModule` registers typed options and one singleton factory. The factory declares provider `inmemory`,
aliases `memory` and `inproc`, priority `-100`, and `IsAutomaticFloor = true`. Vector Core resolves source policy and
one immutable `VectorSpacePlan` before it creates a repository.

The factory owns one bounded store catalog for the application-host lifetime. Vector Core owns a bounded repository
cache keyed by Entity, key type, provider, source, and the complete space shape. Host disposal releases both. There is
no process-global store and no public reset API.

## Storage and hot path

Each physical route stores immutable snapshots keyed by compiled scope identity plus Entity ID. Save clones embeddings
and atomically replaces one complete point under a short store lock. Search takes one point-in-time array snapshot,
applies the compiled metadata predicate, computes exact similarity with `System.Numerics.Tensors`, and sorts by
descending similarity then stable ordinal ID. Caller arrays and metadata cannot mutate stored state.

The adapter never performs provider election, source-policy evaluation, reflection, JSON round-trips, or metadata-shape
compilation on its hot path. Those cross-provider meanings belong to Vector Core.

## Capabilities

The adapter declares kNN, full metadata filters, bulk upsert/delete, normalized scores, and dynamic collections. It
does not declare hybrid search, native continuation, streaming results, multi-vector points, or atomic batch.

Session visibility is immediate. `Sync` is therefore a completed barrier. Eventual spaces are rejected at repository
creation instead of being simulated. `Clear` is scoped data mutation; source policy decides whether it is allowed.

## Bounds

Typed options under `Koan:Data:Vector:InMemory` bound physical spaces, points per space, dimensions, and metadata bytes
per point. Capacity failure occurs before replacement or insertion. Exact search is O(points × dimensions), so these
limits are part of the adapter contract rather than incidental tuning.

There is no external health dependency. Cancellation is checked before mutation and throughout batch/search loops;
host disposal makes later catalog access fail with `ObjectDisposedException`.
