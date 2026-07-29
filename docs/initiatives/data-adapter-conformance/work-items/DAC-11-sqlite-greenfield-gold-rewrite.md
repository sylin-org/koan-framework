---
type: SPEC
domain: data
title: "DAC-11 Build the SQLite Gold Adapter from an Empty Implementation"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-pass-strict-defer
  scope: SQLite replacement, greenfield integrity, connector/Relational/Core behavior; Web rebuild unavailable offline
---

# DAC-11 — Build the SQLite gold adapter from an empty implementation

| Field | Value |
|---|---|
| Phase / kind | gold / ground-up replacement |
| Depends on | DAC-09R-04; later shared changes require a failing SQLite case |
| Unlocks | DAC-13 |
| Primer scope | complete ratified SQLite manifest |
| Production writes | SQLite connector, newly designated SQLite tests/docs, and `evidence/sqlite/**` |
| Owner | Adapter(SQLite) |

## Meaningful outcome

SQLite becomes the lean relational reference: obvious setup, faithful Koan semantics, one native execution path, and
minimal warm-path work.

## Approved vertical-slice exploration

**Task:** Replace the SQLite connector from an empty implementation root and let concrete acceptance failures identify
the minimum Framework or Relational changes.

**Application intent:** Reference SQLite, call `AddKoan()`, and use `Entity<T>` normally with durable, faithful,
bounded local persistence.

**Public expression:** `dotnet add package Sylin.Koan.Data.Connector.Sqlite`; `services.AddKoan();`; then
`await new Todo { Title = "Ship" }.Save(ct)` and `await Todo.Get(id, ct)`. Configuration is optional; an explicit
Source may select `Adapter: sqlite`, a SQLite connection string, lifecycle, and access policy.

**Guarantee/correction:** Managed read/write sources perform exact CRUD, query, count, paging, batch, isolation, and
registered SQLite work with truthful receipts and bounded provider work. Read-only, external-lifecycle, unsupported,
ambiguous, or unprovable operations reject before forbidden I/O or partial mutation.

**Complete intent surface:** No user action exists beyond package reference, `AddKoan()`, the ordinary Entity API, and
optional Source configuration. Adapter registration, mapping compilation, schema realization, connection ownership,
claims, and receipts remain framework/connector responsibilities.

**Public concepts:** No new application concept is introduced. Existing `SqliteOptions` remains only for SQLite-native
configuration; existing Source policy owns lifecycle and access.

**Docs read:** the development primer defines user delight and conformance; the responsibility map assigns policy to
Data, relational mechanics to the family, and native translation to SQLite; DATA-0110 freezes the compact language;
the engineering index and architecture principles require thin adapters and explicit ownership; SQLite README and
TECHNICAL describe compatibility candidates rather than implementation authority.

**Code read:** `IDataRepository`/`IQueryRepository` define the native seam; `MappingPlan` and
`RelationalCommandPlanner` own logical/physical decisions; `RelationalManagedMapping`, `SqlFilterTranslator`,
`RelationalSourceIntegration`, and `RelationalNeutralReader` are reusable family mechanics; the accepted Npgsql and
SQL Server replacements prove that managed and explicit maps can share one repository path. The current SQLite
factory, two repositories, configurator, query compilers, mapped write/schema helpers, neutral reader, source
integration, and readiness caches are retirement evidence only.

**Reusing:** public package/type/configuration identities; Data source resolution and semantic facade; Relational
filter/mapping/schema contracts; shared Entity JSON; Microsoft.Data.Sqlite. No former SQLite helper, control flow,
cache, repository structure, or test structure is reused.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| stable identities and bounds | `Infrastructure/Constants.cs` | one owner for provider identity, configuration keys, and finite limits |
| typed configuration | `SqliteOptions.cs` | the complete SQLite-native configuration surface |
| activation and route compilation | `SqliteAdapterFactory.cs`, `Initialization/SqliteModule.cs` | one connector entry, one immutable source route, and one module |
| host-owned physical connections | `Runtime/SqliteConnections.cs` | canonical file/memory identity, keepers, pools, redaction, and disposal |
| immutable entity/native plan | `Runtime/SqliteEntityPlan.cs` | one managed-or-explicit `MappingPlan`, command planner, dialect, and precompiled hydration state |
| one native execution path | `Runtime/SqliteRepository.cs` | CRUD, query, exact receipts, bulk, conditional write, raw instructions, and nested atomic batch |
| declared-shape realization | `Runtime/SqliteSchema.cs` | the only SQLite DDL and definition-validation owner |
| neutral source topology | `Runtime/SqliteInspector.cs` | SQLite metadata lowering only; registered SQL uses the Relational family owner |
| truthful claims and health | `Runtime/SqliteFeatures.cs`, `SqliteHealthContributor.cs` | one claim declaration and one non-mutating provider probe |

**Coalescence:** the closest pattern is the contract seam used by the accepted Npgsql and SQL Server replacements,
not either provider's file graph. Its useful fact is one `MappingPlan`/`RelationalCommandPlanner` path for managed and
explicit shapes. Specificity is Relational Family for mapping, filtering, neutral rows, and registered SQL; Adapter
for SQLite names, JSON functions, connection modes, schema facts, dispatch, and error codes. Disposition is
`REBUILD`: delete both old repositories and every SQLite-local duplicate of family mechanics. The one target owner is
the new SQLite repository consuming family plans; moving it wider would leak SQLite syntax and moving family
materialization narrower would duplicate shared law.

**Ergonomics:** the common path remains package + `AddKoan()` + `Entity<T>`. IntelliSense exposes no provider assembly
machinery. Adapter code reads as plan, command, execute, receipt; each concern has one discoverable owner.

**Constraints satisfied:** controllers are irrelevant; there are no inline endpoints or placeholders; stable SQLite
identifiers live in connector constants; tunables remain typed options; Entity statics are the user surface; large
reads require provider-bounded paging; README/TECHNICAL and this card change with behavior.

**Risks:** SQLite concurrency/locking, generated keys, JSON path preservation, file aliasing, and schema gates need
real-provider proof. The shared structured-value codec must preserve polymorphic roots and managed fields without a
second object mapper. The 44/44 untouched suite is only a behavioral baseline; it does not prove empty-root lineage,
one execution path, strict packets, fault injection, or stable performance.

## Required work

1. Verify DAC-15's common base, empty SQLite implementation root, ratified contract, target manifest, and complete
   retirement inventory. Use the repository explore workflow, but treat the former adapter only as provider/public
   evidence—not a pattern to preserve.
2. Design from the Framework/Relational contracts outward. List every runtime type, compiled plan, cache, resource
   owner, dispatch boundary, and abstraction in `rewrite/replacement.json`; give each a `contract`,
   `shared-mechanics`, or measured `hot-path` reason.
3. Implement activation, connection/file lifecycle, logical-to-physical mapping, compiled SQL/parameter plans,
   CRUD/query/count/page/bulk/transaction behavior, registered SQL operations, inspection, native receipts, exact
   SQLite failure mapping, cancellation, facts, health, and disposal required by the ratified manifest.
4. Keep reflection, schema discovery, mapping compilation, capability negotiation, and readiness off warm operations.
   Use bounded immutable host-scoped plans and caches; never add sync-over-async, message-text classification,
   swallowed failures, client fallback hidden as native, or redundant JSON passes.
5. Implement claims and declines truthfully. Unsupported behavior rejects before provider work or partial mutation.
6. Replace adapter-specific tests with contract-derived black-box, native-plan, fault, lifecycle, negative, and
   performance cases. Do not port old helper/test structure.
7. Complete the new-source, compile/registration, one-execution-path, moving-parts, evidence, and retirement-absence
   manifests. The accepted change deletes the former implementation atomically; it never contains old and new routes.

## Verification

- Clean build and the complete SQLite, Relational, Data Core, AdapterSurface, and strict Forge suites.
- Native plans/dispatch counts for filters, sorts, pages, counts, indexes, bulk/batch, and named SQL operations.
- ReadOnly/External policy, cancellation/fault, restart/durability, disposal, bounded-cache, and two-host cases.
- Provider-relative cold/warm allocation and elapsed baselines; mutations catch policy bypass, fallback, extra
  dispatch, duplicate registration, inert index, false optimization, and unbounded state.
- `Test-GreenfieldReplacement.ps1` passes only with `startedEmpty: true`, complete retirement, one execution path, no
  shadow path, and justified moving parts.

## Definition of done

- [ ] Every ratified SQLite row passes and every decline fails closed.
- [x] The connector contains only SQLite-native responsibilities behind certified shared contracts.
- [x] One activation, registration, repository/native execution, claim, and adapter-test authority remains.
- [x] Every moving part has a necessary contract/shared-mechanics/hot-path reason.
- [ ] Setup, limits, diagnostics, native behavior, and performance are exemplary and reproducible.

## Implementation checkpoint

- Empty-root replacement integrity: PASS; 16 current source items, eight justified moving parts, 17 retired files,
  one execution path, zero shadow paths.
- SQLite behavior: 47/47, zero skips, including mapping shapes, source policy, inspection/named reads, bulk/batch,
  health, connection lifecycle, filters/paging, isolation, and polymorphic cold restart.
- Shared regressions: Relational 16/16 and Data Core 471/471, zero skips. The Core run disables the unavailable
  Windows Event Log sink in this unprivileged runner; no test behavior is changed.
- Web AdapterSurface: not evidenced. Its required packages are absent from the offline cache, and the available
  binary predates the replacement, so it is explicitly rejected as proof.
- Strict packet and stable performance runner: unavailable; no certificate is claimed.

## Stop conditions

Stop for a missing shared seam, ambiguous public behavior, incomplete retirement inventory, provider limitation hidden
by emulation, need for an old/new bridge, or moving part without a necessary contract or measured hot-path reason.
