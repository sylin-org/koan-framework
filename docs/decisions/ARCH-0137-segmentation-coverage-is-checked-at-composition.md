---
id: ARCH-0137
slug: segmentation-coverage-is-checked-at-composition
domain: Architecture
status: Accepted
date: 2026-10-21
title: Segmentation coverage is checked at composition
related:
  - ARCH-0115
  - ARCH-0136
  - STOR-0011
  - DATA-0105
---

# ARCH-0137: Segmentation coverage is checked at composition

## Context

ARCH-0115 compiles hard segmentation (the `tenant` dimension when Koan.Tenancy is referenced) into one
`SegmentationPlan`, and each state pillar realizes it physically: Data through `DataSegmentationPlan` and the
repository facade, Storage through `StorageIdentityPlan` and the `ScopedStorageService` key-prefix chokepoint. The
decision promises that segmentation "fails closed when no faithful realization exists".

Both pillars installed their realization only when the application had not supplied the pillar's main service:

- `StorageModule.Register` skipped `StorageIdentityPlan` and the `ScopedStorageService` decorator whenever an
  `IStorageService` was already registered, and called that supplied service authoritative. With Tenancy active,
  every tenant read and wrote the same physical keys.
- `RegisterKoanDataCoreServices` returned before any Data registration whenever an `IDataService` was registered,
  so neither the Data realization nor the repository facade existed.

Nothing checked that a pillar which takes part in segmentation actually realized it. The facts listed the
realizations that were registered, so a missing one left no rejection, only an absent row (ledger NK-059).

## Decision

**A pillar that takes part in hard segmentation declares it, and composition refuses a host in which a declared
participant has no realization.**

1. `SegmentationParticipation` (Koan.Core, framework-facing) names a pillar, the realization type that covers it and
   the correction to show when it is missing. A pillar declares it from `Register`, before any branch that could skip
   its realization. Data and Storage declare it.
2. When the segmentation plan compiles with at least one dimension, Core checks the composed collection: every
   declared participant must have its realization registered as an `ISegmentationRealization`. A missing one fails
   the composition with an `InvalidOperationException` that names the dimensions, the pillar and the pillar's
   correction. A plan with no dimensions checks nothing.
3. Storage no longer skips its chokepoint for a supplied `IStorageService`. It registers `StorageIdentityPlan`
   unconditionally and wraps each supplied `IStorageService` registration in `ScopedStorageService`. With no
   dimensions the wrapper passes keys through unchanged.
4. Data cannot wrap a supplied `IDataService`: it replaces Data core entirely. With hard segmentation active, such a
   host is refused at composition; without it, nothing changes.
5. The segmentation facts add a rejection row (`koan.segmentation.realization.rejected`) for a declared participant
   without a realization, for hosts that reach the runtime by a path the composition check cannot see.

## Compatibility

- An application that supplies its own `IStorageService` while Koan.Tenancy is referenced now has its keys prefixed
  by tenant, as Koan's own storage service does. Objects written before this change under unprefixed keys are not
  visible inside a tenant scope; they remain reachable through an explicit host scope. A supplied service that
  already isolates tenants itself is now prefixed twice; the correction is to let Koan's chokepoint own isolation.
- An application that supplies its own `IDataService` while Koan.Tenancy is referenced no longer boots. The message
  says to remove the registration or the Tenancy reference.
- No public signature changes. `SegmentationParticipation` is a new framework-facing type.

## Guarantee and correction

- In a host with an active hard-segmentation dimension, every pillar that declared participation has its
  realization registered, or `AddKoan()` throws before the host is built.
- The correction is named by the pillar: for Data, remove the application's `IDataService` registration; for
  Storage none is needed, because a supplied service is wrapped.

## Evidence

- `SegmentationCoverageSpec` (Koan.Tenancy.Tests): an application `IStorageService` registered before `AddKoan()`
  receives a different physical key per tenant, each tenant reads its own bytes, and the storage realization is
  registered; before this change both tenants wrote one key. An application `IDataService` makes `AddKoan()` throw
  naming segmentation, `data` and `IDataService`; before this change composition succeeded.
- `SegmentationCompositionSpec` (Koan.Core.Tests): a declared participant without a realization fails `Verify` for an
  active dimension and passes for an empty plan or once its realization is registered; a duplicate declaration is
  kept once; the facts projection adds the rejection row.
- Unchanged: Koan.Storage.Core.Tests 20, Koan.Storage.Connector.Local.Tests 31, Koan.Jobs.Tenancy.Tests 16,
  Koan.Tests.Data.Core 582, Koan.Tests.Integration.Bootstrap.Pillars 17.
