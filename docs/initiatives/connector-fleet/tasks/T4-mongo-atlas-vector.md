---
type: GUIDE
domain: data
title: "T4 — Mongo Atlas Vector connector"
audience: [ai-agents]
status: current
last_updated: 2026-08-19
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-19
  status: verified
  scope: paths, naming, oracle command, and acceptance
---

# T4 — Mongo Atlas Vector connector

Read [BOOTSTRAP.md](../BOOTSTRAP.md) first. [ARCH-0127](../../../decisions/ARCH-0127-connector-fleet-strategy.md)
is normative.

**This task is expected to be the most likely to end BLOCKED, and that is an acceptable outcome.** It
is sequenced last for that reason. Read the STOP preconditions carefully before writing anything;
being blocked here costs nothing, while faking a runtime to get past it corrupts the whole initiative.

## Goal

Expose MongoDB Atlas Vector Search as a vector-plane connector, so an application already running Mongo
gains semantic search without adopting a second service.

## STOP preconditions

If any fails: revert, record BLOCKED with which one and what would be required to unblock it, then stop
— T4 is the last task.

1. `VectorAodbConformanceSpecsBase.cs` exists at the path named in BOOTSTRAP.
2. `src/Connectors/Data/Mongo/` (or equivalent) exists — reference for Mongo options, discovery, and
   connection handling.
3. **A container image providing Atlas Vector Search can start in this environment.** Ordinary
   `mongo:latest` does **not** provide vector search. Verify this precondition *first*, before any
   other work, because it is the one most likely to fail.
4. The conformance kit's specs can run against that image without modification. If the kit requires a
   capability the image cannot provide, that is BLOCKED — the kit is not yours to change.

If vector search is only reachable against a hosted Atlas cluster requiring credentials, **stop**. This
initiative does not authorize creating accounts, provisioning cloud resources, or handling credentials.
Record BLOCKED with that as the reason.

## Facts as of authoring (2026-08-19 — verify)

- Same vector-plane layout and naming rules as T1 and T2.
- `Sylin.Koan.Data.Connector.Mongo` ships and is the record-plane connector. It is not yours to modify.
- Atlas Vector Search is a feature of MongoDB Atlas rather than of the MongoDB server generally. Do not
  assume any Mongo deployment provides it, and say so plainly in the connector's README and in the
  capability map's **Also needs** column.

## Required artifacts — exact names

Adapter name `MongoAtlasVector` everywhere, that casing exactly.

- Connector: `src/Connectors/Data/Vector/MongoAtlasVector/`, project
  `Koan.Data.Vector.Connector.MongoAtlasVector.csproj`, package
  `Sylin.Koan.Data.Vector.Connector.MongoAtlasVector`. Mirror the Qdrant layout.
- Conformance: `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.MongoAtlasVector.Tests/`
  containing exactly `MongoAtlasVectorVectorAodbConformanceSpec.cs` — note the doubled `Vector`, which
  is correct: the adapter name ends in `Vector` and the required suffix is
  `VectorAodbConformanceSpec.cs`. Derive from `VectorAodbConformanceSpecsBase`; override
  `BootHostAsync()` and `ProveVectorAnnexCellAsync()` only. Every `[Fact]` remains inherited. Implement
  every V-01 through V-24 outcome pinned in BOOTSTRAP's vector annex proof profile using private
  provider-specific helpers.
- Adapter key: `mongo-atlas-vector`, lowercase.
- Add both projects to `Koan.sln`.
- Create the connector's project-local `version.json` through BOOTSTRAP's new-project exception with
  compatibility line `1.0`, `versionHeightOffset` `0`, and the standard path filters.

## Oracle

```powershell
pwsh scripts/forge-verify.ps1 -Adapter MongoAtlasVector -Plane vector
```

**Must exit `0`.** Exit `2` (skipped) is a failure — a suite that skips because no Atlas runtime was
found is precisely the BLOCKED case, so record it as BLOCKED rather than reporting the task Done.
Exit `3` means the spec filename is wrong; re-read the doubled-`Vector` note above.

## Discoverability

1. Row in the vector table of `docs/reference/capability-map.md`. The **Also needs** column must state
   that it requires an Atlas deployment with vector search — this is the single most misleading thing
   about this connector and the column exists for exactly this.
2. Add the package to every recipe enumerating vector index choices:

```powershell
Select-String -Path docs/recipes/*.md -Pattern 'Data.Vector.Connector'
pwsh scripts/build-recipe-index.ps1
```

## Acceptance

```powershell
pwsh scripts/forge-verify.ps1 -Adapter MongoAtlasVector -Plane vector   # exit 0
dotnet build Koan.sln                                                   # succeeds, no new warnings
pwsh scripts/skills-verify.ps1 -Structure                               # exit 0
pwsh scripts/docs-lint.ps1                                              # Errors: 0
pwsh scripts/build-recipe-index.ps1 -Check                              # exit 0
pwsh scripts/build-connector-matrix.ps1                       # regenerate, then -Check must exit 0
dotnet run --project tools/Koan.Packaging -- quality `
  --output docs/reference/package-quality.json `
  --markdown docs/reference/package-quality.md      # regenerate; commit the result
```

One commit, `feat(connector): mongo atlas vector search on the vector plane`, then the ledger entry.

## Out of scope

- Do not modify the Mongo record connector, or fold vector support into it.
- Do not create cloud accounts, provision hosted clusters, or handle credentials of any kind.
- Do not write an in-memory or emulated substitute for Atlas Vector Search to make specs pass. A
  connector proved against a fake proves nothing, and this is the failure mode this task is most
  exposed to.
- Do not add, rename, or weaken any conformance spec.
- Do not touch `product/claims.json` or describe the connector as assessed.
- Do not update any `AGENTS.md`.

## On finishing

T4 is the last task. When it is Done or BLOCKED, write the final ledger entry, confirm every task is
Done or BLOCKED with none in progress, state in the ledger that the initiative is complete, and stop.
Do not add further connectors — a new connector is a new decision under ARCH-0127, not an extension of
this initiative.
