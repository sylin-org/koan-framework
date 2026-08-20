---
id: DATA-0113
slug: bulk-reads-use-the-strongest-strategy
domain: DATA
status: Accepted
date: 2026-08-20
title: Bulk reads use the strongest strategy the provider supports
related:
  - DATA-0079
  - DATA-0107
  - DATA-0108
---

# DATA-0113: Bulk reads use the strongest strategy the provider supports

## Precedence

This record is authoritative for **bulk-consumer source-read selection**. It amends DATA-0107 only
where that record made bulk consumers inherit public-stream rejection, and DATA-0108 only where that
record required Backup to stream or reject. DATA-0107 continues to govern direct Entity streams;
DATA-0108 continues to govern archive integrity and recovery.

## Application contract

**Business sentence:** copy, move, mirror, or back up the matching Entities without making application
code choose a provider read mechanism.

**Complete expression:** existing calls remain complete; no strategy option or registration is added.

```csharp
await Widget.Copy().To(partition: "archive").Run(ct);
await backup.Create<Widget, string>("before-import", request, ct);
```

The selected provider and, for Backup, the requested storage profile must be available. A qualified
provider guarantees a bounded source stream. A resident/local provider guarantees an explicit
materialized bulk read whose selection is recorded; transfers also return a warning. Direct
`AllStream` / `QueryStream` calls retain DATA-0107's corrective rejection because those APIs promise
streaming, not merely completion.

`BulkRead` is the single internal owner. Application code gains no concept or branch, IntelliSense
stays on the existing Entity and Backup verbs, and runtime facts explain the strategy a human or
agent could not see from the provider-neutral call.

## Outcome

A **bulk read** — one that must touch every matching row — uses a provider-bounded stream when the
routed adapter advertises `DataCaps.Query.ProviderBoundedPaging`, and an explicitly materialized query
when it does not. The choice is never silent: it records a `koan.data.stream.execution` runtime fact
either way, and a consumer may add its own notice.

The decision lives in one owner, `Data<TEntity, TKey>.BulkRead`, so a bulk consumer inherits it rather
than re-deriving it. Both consumers are on it: the transfer DSL (`Copy`/`Move`/`Mirror`, which also
surfaces a `TransferResult.Warnings` entry and keeps its writes batched) and Data Backup.

## Context

DATA-0107 made `AllStream` / `QueryStream` provider-bounded and made unqualified adapters reject
rather than quietly materialize. It considered Data Backup as a consumer and updated it, and it
records "early rejection for InMemory/JSON" as accepted evidence.

It did not consider the transfer DSL, which is the other consumer of the same primitive. The DATA-0107
commit never touched `EntityTransferBuilderBase`, and the ADR does not mention transfer, move, or copy
anywhere. `ReadBatches` kept calling `AllStream` / `QueryStream`, so from that commit onward every
transfer over InMemory, JSON, or Redis threw:

```
Stream for <Entity> was rejected by provider 'json' (missing-provider-bounded-paging).
```

That is not a niche gap. JSON is the Data pillar's low-priority floor — the adapter a bare reference
composes — so `Widget.Move().From(a).To(b)`, a first-class Entity verb, did not work in the default
development configuration. The shared adapter-surface suites caught it immediately and stayed red for
InMemory, JSON, and Redis, which is how it survived: nothing gated on those suites.

### What this overturns

The repository contained two contradictory specifications, and this decision resolves them.

A later commit (`0bd5a0a90`, the adapter rebuild against the golden contract) added
`EntityTransferDsl.Spec.Copy_MissingBoundedPaging_FailsBeforeProviderWork`, which asserted the
rejection deliberately against a synthetic repository with an `AdvertiseBoundedPaging` toggle. It was
green. At the same time the real-adapter conformance kit asserted the opposite — that `Copy`, `Move`,
and `Mirror` work — and was red on three adapters.

Three things decide it against the synthetic spec:

1. The conformance kit runs against **real adapters** and is the product-facing contract for "this verb
   works on this adapter". The synthetic spec tests plumbing through a capability toggle, and its
   substantive property — that no partial provider work happens — is preserved either way.
2. [The capabilities guide](../guides/entity-capabilities-howto.md) documents `Copy`/`Move`/`Mirror`
   with **no adapter boundary at all**, and already documents `result.Warnings` as the inspection
   surface. Under rejection, that guide is simply wrong for a third of the adapters.
3. A first-class Entity verb that fails on the pillar's own floor adapter breaks the "a bare reference
   composes" promise at the first moment a developer would meet it.

The synthetic spec is restated, not deleted: it now pins that an unqualified provider materializes,
reports it, and still bounds its writes.

## Decision

A bulk consumer that must touch every row asks the routing layer what the provider can do, and picks
the strongest strategy available:

| Routed provider | Source read | Reported |
|---|---|---|
| Advertises provider-bounded paging | Streamed, page at a time | selected runtime fact; no consumer warning |
| Does not | One explicitly materialized query | selected runtime fact; transfer warning |

`Data<TEntity, TKey>.BulkRead(...)` owns the choice and resolves the routed provider's capability
**before** the read begins. The alternative — catching `QueryStreamRejectedException` and falling
back — was rejected: the same exception also carries unsupported-sort and offset-overflow reasons,
which are real errors and must still fail the transfer. A strategy decision must not be made by
catching an error that means several different things.

DATA-0107 remains authoritative for direct `AllStream` / `QueryStream` calls: an unqualified adapter
still rejects instead of pretending to stream. This record owns the narrower bulk-consumer decision.
A qualified adapter still streams, so a transfer over a large relational table stays
provider-bounded exactly as before. The fallback reaches only the three adapters DATA-0107 itself
lists under "corrective rejection" — InMemory, JSON, Redis — all of which keep the whole set resident
or local, so a materialized read costs what every other read on them already costs. DATA-0107's own
rejection message names "materialize the query explicitly" as the sanctioned alternative; this is a
consumer taking that route deliberately and declaring it, which is what "Koan never silently returns
to complete-result materialization" asks for.

Data Backup follows the same rule, and this reverses the source-read position DATA-0107 and DATA-0108
recorded for it. That position rested on backup's archive being accumulated in one `MemoryStream` —
but that cost exists whatever the read strategy is, and it is not reduced by refusing to read. What
refusing *did* accomplish was making backup untryable on JSON: a developer who references
`Koan.Data.Backup` in the configuration Koan composes by default could not run it at all. A capability
that cannot be tried where people start is not a safe boundary, it is a dead end.

The memory concern is real and unaddressed either way; it belongs to the archive writer, which is
where DATA-0107 already located it.

## Consequences

- `Copy` / `Move` / `Mirror` and Data Backup work on every adapter, including the pillar's floor.
- A third bulk consumer inherits the strategy by calling `BulkRead`, rather than choosing again.
- A transfer on an unqualified adapter holds the matching set in memory once. That is acceptable for
  the three adapters in question and is stated on the result rather than assumed.
- A future unqualified adapter over a genuinely large store would inherit the materialized path. The
  DATA-0107 qualification table is the gate; an adapter that needs bounded reads must earn the
  capability rather than rely on this fallback.

## Evidence

- `AdapterTransferSpecsBase.Transfer_reports_a_materialized_read_exactly_when_the_provider_cannot_stream`
  asserts **both** directions: a qualified adapter must produce no warning (it still streams), an
  unqualified one must produce one (it never materializes silently). It runs in every adapter-surface
  suite, with `ProviderStreamsAreBounded` overridden to `false` in the InMemory, JSON, and Redis specs.
- InMemory 75/75, JSON 53/53, SQLite 53/53 — previously 7 failures each in InMemory and JSON, and the
  SQLite suite green through the same shared transfer specs, proving the streaming path is unchanged.
- `EntityTransferDsl.Spec.Copy_MissingBoundedPaging_MaterializesTheSourceAndReportsIt` pins the
  restated contract on the synthetic adapter, including that 3 rows at `Batch(2)` still dispatch two
  writes — the read materialized, the writes did not.
- `BackupRecoverySpec.Resident_adapters_back_up_via_a_materialized_read_and_record_it` replaces the
  rejection theory over `inmemory` and `json`: the archive round-trips all five records, and the probe
  shows a single read rather than the `1, 2, 3` page walk the streamed path performs, with the runtime
  fact reporting `materialized-bulk-read`. Backup 9/9, Data Core 474/474.
