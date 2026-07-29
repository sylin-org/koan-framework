---
type: SPEC
domain: data
title: "DAC-43 Evaluate and Certify the SQL Server Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: implementation-accepted
  scope: SQL Server greenfield implementation and provider acceptance
---

# DAC-43 — Evaluate and certify the SQL Server adapter

| Field | Value |
|---|---|
| Phase / kind | fleet / whole-adapter greenfield rebuild |
| Depends on | DAC-30 |
| Primer scope | dynamically selected SQL Server manifest |
| Production writes | SQL Server connector replacement authorized |
| Owner | Adapter(SQL Server); Relational Family rows split |

## Meaningful outcome

Koan proves its relational contract against an independent driver/dialect and the external/legacy-table journey that
motivated source inspection, mapping, and registered reads.

## Execute

1. Pin SQL Server image/version, Microsoft driver, database/schema, read-only/read-write identities, and create
   `evidence/sqlserver/`.
2. Inventory every provider delta and shared Relational path, including bulk copy/batches, transactions, generated/
   composite keys, JSON/flat/hybrid mapping, views, stored artifacts, Direct/instructions, and health.
3. Build a precreated legacy fixture with unusual identifiers, codecs, nested logical mappings, generated values,
   required unmapped fields, view/read-only shape, and named read/scalar. Exercise all four source postures.
4. Use native plans and exact provider error numbers/types for handled/index/failure claims. Prove no external DDL and
   no write before provider I/O under read-only.
5. Run cancellation, timeout, deadlock/conflict, connection loss, commit uncertainty, pool saturation, disposal,
   restart, soak, and provider-relative baselines in the heavy lane.
6. RED creates one-owner Relational or SQL Server remediation cards and blocks; no production edit in this card.

## Approved vertical-slice exploration

**Task:** Replace the SQL Server connector from an empty implementation root, using retired code only for public facts,
provider constraints, regression cases, and negative lessons.

**Application intent:** Reference SQL Server, call `AddKoan()`, and use ordinary `Entity<T>` persistence; optionally
map an aggregate to an existing table, inspect an external source, or register a bounded SQL read.

**Public expression:** Managed persistence is package + `AddKoan()` + `Entity<T>`. External integration uses source
configuration, `Source(...).Map<T>(...)`, `Data.Source(...).Inspect()`, and
`Query`/`Scalar(..., query => query.Sql(...))`. No repository, `SqlConnection`, or provider registration appears in
application code.

**Guarantee/correction:** One immutable `MappingPlan` drives both managed Id+object storage and explicit legacy shapes.
SQL Server executes native parameterized CRUD, filter, order, page, count, bulk, conditional write, and transactions
with exact receipts. ReadOnly rejects writes before readiness or provider I/O; External performs no DDL. Unsupported
operations reject before unbounded work or partial mutation.

**Complete intent surface:** Package reference, `AddKoan()`, reachable SQL Server, optional source policy/configuration,
and optional compact map or registered SQL declaration are the complete user actions.

**Public concepts:** No new public concept is required. Existing provider-neutral Data mapping, source, inspection,
record, and registered-operation concepts cover the full user decision surface.

**Docs read:** Architecture principles require Entity-first intent, one compiled decision, and capability honesty. The
adapter primer defines whole-adapter replacement, source safety, mapping shapes, exact receipts, and warm-path limits.
DATA-0110 fixes the compact grammar. The responsibility map keeps mapping/policy in Data, common relational mechanics
in the Family, and T-SQL/types/error codes in this adapter.

**Code read:** `IDataRepository`, `IQueryRepository`, `MappingPlan`, `RelationalCommandPlanner`,
`SqlFilterTranslator`, and source-integration contracts are the only implementation seams. SQLite is behavioral
evidence, not a code template. The retired SQL Server repository, DDL executors, caches, helpers, and test-specific
branches are not implementation inputs.

**Reusing:** Ratified package/provider/configuration identities; Microsoft.Data.SqlClient; Data source policy, mapping
plans, query coordination, receipts, managed fields, naming, operation catalog, and Relational symbolic contracts. No
current SQL Server execution class or body is retained.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| activation, options, discovery, route, health | `src/Connectors/Data/SqlServer` | one package and route authority |
| one entity plan, repository, dialect, schema, and batch | `src/Connectors/Data/SqlServer/Runtime` | one managed/mapped T-SQL path |
| source inspection and registered SQL execution | `src/Connectors/Data/SqlServer/Runtime` | SQL Server metadata and dispatch |
| relational structured-value and neutral-reader mechanics | `src/Koan.Data.Relational` | identical family semantics proved by both server adapters |

**Coalescence:** Closest pattern is the contract seam formed by `MappingPlan` plus `RelationalCommandPlanner`, not an
existing adapter. Specificity is Adapter for T-SQL and driver work, Family only for provider-neutral relational value
and reader mechanics. Disposition is `REBUILD`; the old 1,000-line repository, DDL executors/store features, telemetry
wrapper, source-blind health, and readiness cache are deleted. A wider owner would leak SQL Server semantics; a
narrower helper split would recreate ceremony without a separate lifecycle.

**Ergonomics:** Managed and legacy storage share one mental model and execution path. The application sees only compact
Data language; SQL Server-specific terms appear solely in configuration, native SQL bindings, and diagnostics.

**Constraints satisfied:** Entity statics remain the user surface; no HTTP work is involved; stable identifiers remain
connector constants and tunables typed options; large reads are provider-paged; there are no placeholders, shadow
paths, unbounded caches, or sync-over-async bridges; README and TECHNICAL change with behavior.

**Risks:** SQL Server identifier/type differences, JSON path updates, generated/composite keys, transaction outcomes,
inspection bounds, and exact `SqlException.Number` classification require real-server proof. The default container is
heavy, so verification stays sequential.

## Verification

- Full SQL Server/Relational/shared suites, strict Forge, packet validation, least-privilege roles, native plans, and
  heavy fault/lifecycle cells execute.

### Implementation acceptance — 2026-07-28

- The retired SQL Server connector implementation was removed, including the old repository, DDL executor/store
  feature layer, options configurator, telemetry wrapper, and duplicate readiness state.
- Real SQL Server suite: 33/33 against the pinned SQL Server 2025 container. Coverage includes compact external maps,
  nested JSON preservation, composite/generated keys, policy enforcement, registered SQL, neutral inspection, and
  bounded sampling.
- Relational family suite: 16/16. Connector and test projects build with zero warnings.
- Named SQL requires a declared read lane. Koan uses the configured lane connection and a rollback-only transaction;
  least-privilege database grants remain the SQL Server security boundary.
- Full heavy certification remains open: dedicated least-privilege identities, faults/cancellation/deadlocks, pool
  saturation, restart, soak, native plan captures, and strict Forge were not executed in this slice.

## Definition of done

- [ ] SQL Server is green for its exact manifest and legacy/external examples.
- [x] Driver/dialect behavior remains local; shared behavior resolves to Relational evidence.
- [ ] Heavy-lane prerequisites and timing are recorded for CI/release use.

## Stop conditions

Missing heavy provider, inability to prove read-only/external permissions, message-based failure classification, or
production changes block certification.
