# ARCH-0112 — Capability-aware bounded relationship negotiation

**Status**: Accepted
**Date**: 2026-07-14
**Deciders**: Framework maintainer
**Scope**: Child relationship queries across Entity, batch, Web, and MCP surfaces
**Related**: ARCH-0105, ARCH-0106, ARCH-0111, R04-06

---

## Context

Koan described which filter operators an adapter could evaluate, but not how it evaluated them.
Relational and document adapters translated filters into backend queries; InMemory evaluated its
already-resident object store; JSON and Redis scanned their keyspaces. All could correctly advertise
the same `FilterSupport.Full` semantics.

Relationship helpers then confused semantic support with execution cost. Entity and batch paths loaded
every child and filtered locally. Web built the correct authorization-aware filter, but still inherited
the generic residual fallback and queried each root independently. The code looked business-shaped
while hiding an unbounded operational decision.

## Decision

### 1. Filter semantics and physical execution are separate capabilities

`DataCaps.Query.Filter` continues to carry `FilterSupport`: which normalized filter nodes are correct.
`DataCaps.Query.FilterExecution` carries `FilterExecutionProfile`: whether those nodes execute as
`Native`, `InMemory`, `Scan`, or an unknown posture, and whether the provider can enforce a candidate
bound.

Current declarations are:

- SQLite, SQL Server, Postgres, Mongo, and Couchbase: `Native`;
- InMemory: `InMemory` with bounded-candidate support;
- JSON and Redis: `Scan` with bounded-candidate support.

These are declarations, not fleet certification. R04-06 executes the InMemory, JSON, and SQLite cells.

### 2. One executor owns child relationship decisions

`IRelationshipQueryExecutor` receives one or many parent keys, the child reference property, an
optional additional filter, and `RelationshipQueryPolicy`. It creates one normalized `Eq`/`In` query
per relationship edge and selects:

- `Native` when the complete predicate is pushed to a native backend;
- `InMemory` when the provider's source of truth is already an in-process store;
- `BoundedScan` when the caller explicitly accepts a finite candidate budget and the provider enforces
  it before returning rows;
- `BoundedFallback` when a native provider cannot push the complete predicate, the caller explicitly
  accepts a finite candidate budget, and the pushable candidate count fits it;
- rejection for unknown posture, an implicit scan, a missing bounded seam, an exceeded candidate
  budget, or an exceeded result budget.

Strict is the default. `RelationshipQueryPolicy.Bounded(maxCandidates, maxResults)` is an explicit
opt-in, not a global compatibility switch. A provider never returns a partial relationship when a
bound is exceeded.

### 3. Every public relationship path uses the executor

`Entity.GetChildren`, scalar/set/stream `Entity.Relatives`, governed Web expansion, and the
MCP entity surface converge on the executor. Existing Entity method shapes remain and use strict
policy; overloads accept an explicit policy. Web batches all roots into one child query per edge,
retains the related type's authorization predicates, and defaults to 200 results per edge with no
scan fallback. Applications can configure `EntityEndpointOptions.RelationshipMaxResults` and
`RelationshipFallbackMaxCandidates`.

Parent lookup remains a keyed read. This decision does not claim batched parent lookup, arbitrary
graph planning, recursive depth budgets, or index verification.

### 4. Rejection is corrective and inspectable

`RelationshipQueryRejectedException` exposes safe typed fields: relationship, provider, stable reason,
correction, and optional limit. Web maps unsupported/unbounded execution to 422 and exceeded limits to
413; MCP receives the same endpoint result through its existing translator.

The executor writes the latest `koan.data.relationship.execution` capability fact. The subject is the
stable relationship shape, never an entity key. Selected and rejected operation facts update the
host snapshot exposed by Web and `koan://facts`. A request-level capability rejection is inspectable
but does not degrade readiness; only facts whose kind is rejection/degradation, or a collection
failure, affect the runtime-facts health contributor.

## Consequences

- Business-shaped Entity code no longer conceals a whole-store child scan.
- Authorization/read-scope filters still pass through `RepositoryFacade`; the bounded seam cannot
  bypass guards, managed read filters, or field reversal.
- Scan-backed applications may see a new loud failure. The correction is finite and local: choose an
  explicit candidate budget or a native-filter provider.
- Redis bounded execution limits enumerated/fetched candidates; it is not a claim that every server
  cursor operation has constant cost.
- Runtime facts are a latest-state snapshot, not a request audit log or metrics system.
- Full provider-fleet parity, index sufficiency, parent batching, recursive graph budgets, and native
  execution benchmarks remain future work.

## Verification

- Data Core: InMemory selection, SQLite native execution, JSON strict rejection and bounded success,
  candidate overflow without partial rows, Entity-first overloads, cancellation, result grouping,
  and safe facts; full suite 299/299.
- Core facts: stable replacement, sequence/completion preservation, and non-degrading operation-level
  capability rejection, plus recollection without loss of operation facts; focused suite 7/7.
- Web: related-type authorization and walled-edge behavior remain green; result-limit rejection emits
  413 plus the shared fact; focused suite 7/7.
- MCP: relationship visibility 2/2 and conformance 73/73 use the shared endpoint service.
