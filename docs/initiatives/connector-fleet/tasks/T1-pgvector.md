---
type: GUIDE
domain: data
title: "T1 — pgvector connector"
audience: [ai-agents]
status: current
last_updated: 2026-08-19
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-19
  status: verified
  scope: paths, naming, oracle command, and acceptance
---

# T1 — pgvector connector

Read [BOOTSTRAP.md](../BOOTSTRAP.md) first. [ARCH-0127](../../../decisions/ARCH-0127-connector-fleet-strategy.md)
is normative and is not restated here.

## Goal

Expose Postgres's `pgvector` extension as a vector-plane connector, so an application already running
Postgres gains semantic search **without adopting a second service**. That sentence is the whole point
of the task; if the result still requires a separate vector service, the task has failed regardless of
which tests pass.

## Delivery evidence

- **Application intent:** an application already operating Postgres can persist and search Entity
  embeddings without operating a second vector service.
- **Public expression:** reference `Sylin.Koan.Data.Vector.Connector.PgVector`, call `AddKoan()` once,
  select adapter `pgvector` on the intended data source, declare the Entity's vector space, then use
  `Vector<TEntity>.Save`, `Get`, `Search`, `Delete`, and lifecycle operations. Configuration supplies
  the Postgres connection string; the runtime supplies Postgres with the `vector` extension.
- **Guarantee and correction:** awaited Session mutations, exact similarity search, native metadata
  prefiltering, and Koan source/partition isolation are guaranteed. Missing extension, incompatible
  native schema, unsupported visibility/search intent, or unavailable Postgres fails at the adapter
  boundary with a corrective exception or readiness result.
- **Complete intent surface:** package reference, one `AddKoan()` composition, source configuration,
  vector-space declaration, and a reachable pgvector runtime; no provider registration or record-store
  change is required.
- **Coalescence:** keep vector planning, scope decoration, neutral metadata, and capability policy in
  the shared vector pillar; rebuild only Qdrant's backend-mechanics shape as a PgVector adapter. The
  Postgres record connector remains independent because record and vector lifecycles differ.
- **Ergonomics:** `pgvector` is the only new application-visible selector; provider mechanics remain
  behind the existing `Vector<TEntity>` vocabulary and explain themselves through facts and health.

## STOP preconditions

Check each before editing anything. If any fails, revert, record BLOCKED in the ledger with which one
failed, and move to T2. Do not improvise around a failure.

1. `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/VectorAodbConformanceSpecsBase.cs`
   exists and declares `protected abstract Task<(IntegrationHost? host, string? skip)> BootHostAsync();`
2. `src/Connectors/Data/Vector/Qdrant/` exists — it is your structural reference.
3. `src/Connectors/Data/Postgres/` exists — reference for Postgres options, discovery, and connection
   handling.
4. `scripts/forge-verify.ps1` exists.
5. A container runtime is available **and** an image providing Postgres with the `vector` extension can
   start. If no such image can run in this environment, that is a BLOCKED, not a reason to write a
   fake or in-memory substitute.

## Facts as of authoring (2026-08-19 — verify against the tree)

- Vector connectors live at `src/Connectors/Data/Vector/<Name>/`.
- Package identifiers follow `Sylin.Koan.Data.Vector.Connector.<Name>`; namespaces stay `Koan.*`.
- Shipped vector connectors: InMemory, Milvus, Qdrant, SqliteVec, Weaviate.
- `Qdrant` file layout, which you are matching:
  `<Name>Options.cs`, `<Name>OptionsConfigurator.cs`, `<Name>VectorAdapterFactory.cs`,
  `<Name>HealthContributor.cs`, `Discovery/<Name>DiscoveryAdapter.cs`, `Infrastructure/Constants.cs`,
  `Initialization/<Name>VectorModule.cs`, `Runtime/<Name>Client.cs`, `Runtime/<Name>Filter.cs`,
  `Runtime/<Name>Repository.cs`, `Runtime/<Name>Route.cs`, `README.md`, `TECHNICAL.md`, `version.json`.
- Test projects live at `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.<Name>.Tests/`.
- **Registration is by filename.** `forge-verify.ps1` finds a vector target by locating a file named
  `<Adapter>VectorAodbConformanceSpec.cs` and running the nearest `.csproj` above it. There is no list
  to edit anywhere.

## Required artifacts — exact names

Use `PgVector` as the adapter name everywhere, with no variation in casing.

**Connector** — `src/Connectors/Data/Vector/PgVector/`, project file
`Koan.Data.Vector.Connector.PgVector.csproj` producing package
`Sylin.Koan.Data.Vector.Connector.PgVector`. Mirror the Qdrant layout listed above, substituting
`PgVector` for `Qdrant`. Ship a `README.md` and `TECHNICAL.md` matching the shape of Qdrant's.

**Conformance** — `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.PgVector.Tests/`
containing a file named **exactly** `PgVectorVectorAodbConformanceSpec.cs`. It declares one sealed
class deriving from `VectorAodbConformanceSpecsBase`. Override `BootHostAsync()` and
`ProveVectorAnnexCellAsync()` only. Every `[Fact]` remains inherited — do not add, rename, override,
or skip a conformance test. Implement every V-01 through V-24 outcome pinned in BOOTSTRAP's vector
annex proof profile, using private provider-specific helpers. Supply a container fixture beside it,
following `QdrantTestFactory`, that returns an unavailability reason rather than throwing when no
runtime is present.

**Adapter identifier** — the configuration adapter key is `pgvector`, lowercase, matching the pattern
where Qdrant uses `qdrant`.

**Solution** — add both new projects to `Koan.sln`.

**Version ownership** — create the connector's project-local `version.json` using BOOTSTRAP's narrow
new-project exception: compatibility line `1.0`, `versionHeightOffset` `0`, and the standard path
filters. Do not edit an existing version file.

## Oracle

The single acceptance signal for the connector itself:

```powershell
pwsh scripts/forge-verify.ps1 -Adapter PgVector -Plane vector
```

**Must exit `0`.** Exit `2` means specs were skipped and is a failure here — skipped conformance is
not conformance. Exit `3` means the runner did not find your target, which almost always means the
spec filename is wrong.

## Discoverability — required, not optional

A connector nobody can find is not shipped. Both of these:

1. Add a row to the vector table in `docs/reference/capability-map.md`, matching the existing rows'
   shape. Fill the **Also needs** column honestly: state that it requires a Postgres instance with the
   `vector` extension available.
2. Add `Sylin.Koan.Data.Vector.Connector.PgVector` to the vector-index ingredient of every recipe that
   enumerates vector index choices. Find them literally — do not guess:

```powershell
Select-String -Path docs/recipes/*.md -Pattern 'Data.Vector.Connector'
```

Then regenerate the index:

```powershell
pwsh scripts/build-recipe-index.ps1
```

## Acceptance

All five, in this order:

```powershell
pwsh scripts/forge-verify.ps1 -Adapter PgVector -Plane vector   # exit 0
dotnet build Koan.sln                                            # succeeds, no new warnings
pwsh scripts/skills-verify.ps1 -Structure                        # exit 0
pwsh scripts/docs-lint.ps1                                       # Errors: 0
pwsh scripts/build-recipe-index.ps1 -Check                       # exit 0
pwsh scripts/build-connector-matrix.ps1                       # regenerate, then -Check must exit 0
dotnet run --project tools/Koan.Packaging -- quality `
  --output docs/reference/package-quality.json `
  --markdown docs/reference/package-quality.md      # regenerate; commit the result
```

Then one commit, message `feat(connector): pgvector on the vector plane`, and a ledger entry.

## Out of scope — do not do these

- Do not modify the Postgres record connector. Read it; leave it.
- Do not add pgvector support inside the Postgres package. ARCH-0127 settles this: it is a separate
  package on the vector plane.
- Do not add, rename, or weaken any conformance spec.
- Do not touch `product/claims.json`, or describe the connector as assessed, supported, or production
  ready anywhere. It ships unclaimed.
- Do not benchmark, tune, or add indexes beyond what conformance requires.
- Do not update any `AGENTS.md`.
