---
type: SPEC
domain: data
title: "DAC-42 Evaluate and Certify the PostgreSQL Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: implementation-accepted
  scope: PostgreSQL greenfield implementation and provider acceptance
---

# DAC-42 — Evaluate and certify the PostgreSQL adapter

| Field | Value |
|---|---|
| Phase / kind | fleet / whole-adapter greenfield rebuild |
| Depends on | DAC-30 |
| Unlocks | DAC-44 |
| Primer scope | dynamically selected PostgreSQL manifest |
| Production writes | PostgreSQL connector and Npgsql family replacement authorized |
| Owner | Adapter(PostgreSQL); Relational/Npgsql rows split |

## Meaningful outcome

The first networked relational descendant proves that SQLite's gold architecture survives a pooled server driver,
native PostgreSQL types, permissions, transactions, and failure modes.

## Execute

1. Pin PostgreSQL image/version, Npgsql driver, roles, schema/database fixtures, and create `evidence/postgresql/`.
2. Inventory the adapter delta separately from `Koan.Data.Relational` and `Koan.Data.Relational.Npgsql`.
3. Exercise all source postures with provider-enforced read-only/external roles and precreated schemas.
4. Run every selected mapping shape, identity/codec boundary, query/count/page/bulk/batch/conditional/transaction,
   inspection/RecordSet/named read, plan/index, cancellation/fault, pool/disposal/restart/soak, and baseline cell.
5. Preserve `EXPLAIN`/native command and dispatch evidence for handled/index/bulk claims. Exact SQLSTATE/type decides
   failures; message text does not.
6. Family RED generates Relational or Npgsql remediation cards; adapter RED generates PostgreSQL cards. BLOCK and rerun
   in a fresh session after fixes; do not edit production here.

## Approved vertical-slice exploration

**Task:** Replace the PostgreSQL connector and its Npgsql execution family from empty implementation roots, using the
retired code only for provider facts, public compatibility, failure cases, and negative lessons.

**Application intent:** Reference PostgreSQL, call `AddKoan()`, and use ordinary `Entity<T>` persistence; optionally
map an aggregate to an existing PostgreSQL table, inspect an external source, or register a bounded SQL read.

**Public expression:** Managed persistence is package + `AddKoan()` + `Entity<T>`. External integration is expressed
only through source configuration, `Source(...).Map<T>(...)`, `Data.Source(...).Inspect()`, and
`Query`/`Scalar(..., query => query.Sql(...))`. PostgreSQL, Npgsql, repositories, and driver registration never enter
application code.

**Guarantee/correction:** One immutable `MappingPlan` drives managed Id+object storage and explicit legacy shapes.
PostgreSQL executes native parameterized CRUD, filter, order, page, count, bulk, conditional write, and transactions
with exact receipts. ReadOnly rejects writes before readiness or provider I/O; External performs no DDL. Unsupported
operations reject correctively instead of scanning, emulating, or partially mutating.

**Complete intent surface:** Package reference, `AddKoan()`, reachable PostgreSQL, optional source policy/configuration,
and optional compact map or registered SQL declaration are the complete user actions.

**Public concepts:** No new public concept is required. Source, lifecycle, access, Map, Container, Key, Property,
Object, Name, Path, Query, Scalar, and Sql already express every application decision.

**Docs read:** Architecture principles require Entity-first intent, one compiled decision, and honest capabilities.
The adapter primer defines greenfield replacement, four mapping shapes, source safety, exact receipts, and warm-path
limits. DATA-0110 fixes the compact grammar. The responsibility map assigns mapping compilation to Data, relational
symbolic plans to the Family, and Npgsql operations/types/errors to the provider family.

**Code read:** `IDataRepository`, `IQueryRepository`, `MappingPlan`, `RelationalCommandPlanner`,
`SqlFilterTranslator`, and source-integration contracts are the implementation seams. SQLite is behavioral evidence,
not source structure. The retired Npgsql split repository, mapped repository, DDL helpers, caches, and control flow are
not implementation inputs.

**Reusing:** Ratified package/provider/configuration identities; Npgsql dependency; Data source policy, mapping plans,
query coordination, receipts, managed fields, naming, operation catalog, and Relational symbolic contracts. No current
PostgreSQL/Npgsql execution class or body is retained.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| activation, options, discovery, route, health | `src/Connectors/Data/Postgres` | one PostgreSQL package boundary |
| source inspection and registered SQL execution | `src/Connectors/Data/Postgres/Runtime` | provider metadata and dispatch |
| one Npgsql entity plan and repository | `src/Koan.Data.Relational.Npgsql/Runtime` | one managed/mapped execution path |
| Npgsql dialect, schema, batch, and value binding | `src/Koan.Data.Relational.Npgsql/Runtime` | PostgreSQL-wire mechanics shared with Cockroach |
| relational structured-value and neutral-reader mechanics | `src/Koan.Data.Relational` | identical family semantics proved by both server adapters |

**Coalescence:** Closest pattern is the contract seam formed by `MappingPlan` plus `RelationalCommandPlanner`, not an
existing adapter. Specificity is Family for PostgreSQL-wire execution and Adapter for PostgreSQL source topology.
Disposition is `REBUILD`; the two old Npgsql repository paths, compatibility DDL path, duplicate mapping helpers,
telemetry wrapper, and readiness cache are deleted. The next wider owner is wrong for Npgsql SQL/types; the next
narrower PostgreSQL owner would duplicate Cockroach's identical wire mechanics.

**Ergonomics:** Managed and legacy storage have one mental model and one runtime path. IntelliSense remains the compact
Data grammar; an agent maps every user decision directly to one existing descriptor. No provider-specific concept is
added to the ordinary path.

**Constraints satisfied:** Entity statics remain the user surface; no HTTP work is involved; stable identifiers remain
connector constants and tunables typed options; large reads are provider-paged; there are no placeholders, shadow
paths, unbounded caches, or sync-over-async bridges; README and TECHNICAL change with behavior.

**Risks:** Cockroach consumes the public Npgsql family construction seam and must keep compiling, but its provider
behavior will be certified separately. PostgreSQL type/JSON encoding, generated/composite keys, transaction outcome,
inspection bounds, and provider error classification require real-server proof.

## Verification

- Complete PostgreSQL, Relational, Data shared suites and strict Forge execute against the pinned server.
- Least-privilege negative identities prove policy independently of Framework guards.

### Implementation acceptance — 2026-07-28

- The retired PostgreSQL connector and Npgsql execution bodies were removed. The replacement has one compiled entity
  plan and one repository path for managed and explicit maps.
- Real PostgreSQL suite: 26/26 against `postgres:18.4-alpine`, including compact legacy maps, external/read-only
  policy, registered SQL in a native read-only transaction, neutral inspection, and bounded sampling.
- Relational family suite: 16/16. The Cockroach consumer compiles against the new Npgsql construction seam.
- Both connector and test projects build with zero warnings.
- Full heavy certification remains open: least-privilege role matrix, faults/cancellation, pool saturation, restart,
  soak, native plan captures, and strict Forge were not executed in this slice.

## Definition of done

- [ ] PostgreSQL packet is green with exact Family/Adapter ownership.
- [x] Npgsql shared evidence is reusable by Cockroach without hiding Cockroach provider deltas.
- [ ] Claims/docs/facts reflect the actual PostgreSQL version and permissions.

## Stop conditions

Skipped container, unavailable permission posture, Npgsql Family RED, false plan claim, or any production edit blocks.
