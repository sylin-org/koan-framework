---
type: GUIDE
domain: data
title: "T2 — Redis vector connector"
audience: [ai-agents]
status: current
last_updated: 2026-08-19
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-19
  status: verified
  scope: paths, naming, oracle command, and acceptance
---

# T2 — Redis vector connector

Read [BOOTSTRAP.md](../BOOTSTRAP.md) first. [ARCH-0127](../../../decisions/ARCH-0127-connector-fleet-strategy.md)
is normative. T1 established the pattern — follow the connector you built there as well as Qdrant.

## Goal

Expose Redis's vector search as a vector-plane connector, so an application already running Redis gains
semantic search without adopting a second service. Redis already ships as a record connector *and* as a
cache adapter, so for many applications this adds a capability to a process they are already operating.

## STOP preconditions

If any fails: revert, record BLOCKED with which one, move to T3.

1. `VectorAodbConformanceSpecsBase.cs` exists at the path named in BOOTSTRAP.
2. `src/Connectors/Data/Vector/Qdrant/` exists as structural reference.
3. A Redis connector already exists in `src/Connectors/Data/Redis/` (or equivalent) — locate it; it is
   your reference for Redis options, discovery, and connection handling.
4. A container runtime is available **and** an image providing Redis *with vector search* can start.
   Plain `redis:latest` does not include it. If no suitable image runs here, that is BLOCKED — do not
   substitute an in-memory fake.

## Facts as of authoring (2026-08-19 — verify)

- Same layout and naming rules as T1; re-read that task's "Facts" section rather than assuming.
- Redis appears twice in the shipped inventory already: `Sylin.Koan.Data.Connector.Redis` and
  `Sylin.Koan.Cache.Adapter.Redis`. Neither is the vector plane, and neither is yours to modify.
- `Sylin.Koan.Redis` exists as shared Redis backend lifecycle, discovery, and connection pooling.
  Prefer reusing it over opening a second connection path; verify what it actually offers before
  depending on it, and record a deviation if it cannot serve this need.

## Required artifacts — exact names

Adapter name `RedisVector` everywhere, no casing variation.

- Connector: `src/Connectors/Data/Vector/RedisVector/`, project
  `Koan.Data.Vector.Connector.RedisVector.csproj`, package
  `Sylin.Koan.Data.Vector.Connector.RedisVector`. Mirror the Qdrant file layout.
- Conformance: `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.RedisVector.Tests/`
  containing exactly `RedisVectorVectorAodbConformanceSpec.cs` — one sealed class deriving from
  `VectorAodbConformanceSpecsBase`, overriding `BootHostAsync()` only.
- Adapter key: `redis-vector`, lowercase.
- Add both projects to `Koan.sln`.

## Oracle

```powershell
pwsh scripts/forge-verify.ps1 -Adapter RedisVector -Plane vector
```

**Must exit `0`.** Exit `2` (skipped) is a failure. Exit `3` means the spec filename is wrong.

## Discoverability

1. Row in the vector table of `docs/reference/capability-map.md`. In **Also needs**, state honestly
   that it requires a Redis deployment with vector search available — not every Redis has it.
2. Add the package to every recipe enumerating vector index choices:

```powershell
Select-String -Path docs/recipes/*.md -Pattern 'Data.Vector.Connector'
pwsh scripts/build-recipe-index.ps1
```

## Acceptance

```powershell
pwsh scripts/forge-verify.ps1 -Adapter RedisVector -Plane vector   # exit 0
dotnet build Koan.sln                                              # succeeds, no new warnings
pwsh scripts/skills-verify.ps1 -Structure                          # exit 0
pwsh scripts/docs-lint.ps1                                         # Errors: 0
pwsh scripts/build-recipe-index.ps1 -Check                         # exit 0
dotnet run --project tools/Koan.Packaging -- quality `
  --output docs/reference/package-quality.json `
  --markdown docs/reference/package-quality.md      # regenerate; commit the result
```

One commit, `feat(connector): redis vector search on the vector plane`, then the ledger entry.

## Out of scope

- Do not modify `Sylin.Koan.Data.Connector.Redis`, `Sylin.Koan.Cache.Adapter.Redis`, or
  `Sylin.Koan.Redis`. Read them; leave them.
- Do not fold vector support into the existing Redis record connector.
- Do not change the assessment status of any Redis package. `Cache.Adapter.Redis` being unassessed is
  known and is tracked elsewhere; it is not your task and not a defect to fix here.
- Do not add, rename, or weaken any conformance spec.
- Do not update any `AGENTS.md`.
