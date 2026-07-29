---
type: SPEC
domain: data
title: "DAC-01 Audit the Complete Koan.Data Public Surface"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: framework audit prompt and known surface anchors
---

# DAC-01 — Audit the complete Koan.Data public surface

| Field | Value |
|---|---|
| Phase / kind | foundation / audit |
| Depends on | DAC-00 |
| Unlocks | DAC-02 |
| Primer scope | all A–H/P IDs; §10 in full |
| Production writes | forbidden |
| Owner | Framework audit |

## Meaningful outcome

Every Koan.Data entry path is accounted for before the framework is redesigned, including alternate paths that could
bypass source policy or overstate provider work.

## Required work

1. Freeze the DAC-00 identity and build the full `SUR-*` inventory for:
   - Entity statics/instances and `RepositoryFacade`;
   - repositories, query/count/stream, batches, bulk, conditional writes, transactions, transfers, and lifecycle;
   - `EntityContext`, source/adapter/partition routing, axes, naming, readiness, health, and facts;
   - Direct/raw/instruction/patch/connection-override paths;
   - mapping, projections, indexes, codecs, polymorphism, managed fields, and relational orchestration; and
   - current AdapterSurface/Forge and product/public claims.
2. Map every surface to atomic primer rows with Framework/Family/Adapter ownership. Record absent target contracts as
   Target RED; do not invent APIs in this audit.
3. Explicitly investigate the current known seams:
   - flat source settings in `DataSourceRegistry.cs`;
   - `RepositoryFacade` readiness ordering;
   - operation-probe/provision/replay and message classification in `DataAdapterReadinessExtensions.cs`;
   - Direct dictionary materialization and JSON round-trip projection;
   - `ProjectionResolver` scope/cache behavior;
   - `TransactionCoordinator` atomicity language;
   - `KeyValueStore` scan-backed handled claims; and
   - `DataCaps` and query execution receipts.
4. Search for every target vocabulary item from primer §§1–3 (`Data.Source`, `StorageLifecycle`, `Access`, `ReadLanes`,
   inspection, `RecordSet`, `Query`, `Scalar`, `Lane`, `Template`, `Map`, `Container`, `Key`, `Property`, `Name`,
   `Path`, `Object`, and mapping/operation plans) and record exact present/absent evidence.
5. Produce the framework packet: identity, claim scope, surface inventory, claim-to-cell matrix, atomic scorecard,
   evidence registry, and remediation dispositions. Do not recommend adapter-local fixes.

## Verification

- Re-derive public types/members using source/MSBuild/reflection inventory, not a hand list.
- Every discovered execution path has exactly one `SUR-*` row and linked acceptance cases.
- All 81 stable IDs are dispositioned as Observed, Target, Declined, or inapplicable with a mechanical reason.
- A second reviewer searches specifically for missed Direct, instruction, transaction, background, initialization,
  and provider-extension paths.

## Definition of done

- [x] The complete framework packet reproduces and has no unowned or unmapped public execution surface.
- [x] Framework, Family, and Adapter findings are split rather than hidden behind shared ownership.
- [x] DAC-02 receives a finite list of public API/semantic decisions; no implementation change occurred.

## Stop conditions

Stop if DAC-00 identity changed, the primer revision changed, or an execution surface cannot be classified without a
public semantic decision. Record the ambiguity for DAC-02 rather than guessing.
