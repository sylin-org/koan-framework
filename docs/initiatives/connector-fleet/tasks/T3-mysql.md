---
type: GUIDE
domain: data
title: "T3 — MySQL / MariaDB connector"
audience: [ai-agents]
status: current
last_updated: 2026-08-19
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-19
  status: verified
  scope: paths, naming, oracle command, and acceptance
---

# T3 — MySQL / MariaDB connector

Read [BOOTSTRAP.md](../BOOTSTRAP.md) first. [ARCH-0127](../../../decisions/ARCH-0127-connector-fleet-strategy.md)
is normative.

This is the **record** plane, not the vector plane. The kit, the spec filename, and the reference
adapter all differ from T1 and T2. Do not carry those tasks' details across.

## Goal

Close the largest absence in the record plane. Eleven data connectors ship and MySQL is not among them,
despite being one of the most widely deployed databases in existence.

## STOP preconditions

If any fails: revert, record BLOCKED with which one, move to T4.

1. `tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs`
   exists.
2. A relational record connector exists as structural reference — locate the Postgres connector under
   `src/Connectors/Data/` and read it before designing anything. Prefer it over SqlServer as the model.
3. Shared relational infrastructure exists — `Sylin.Koan.Data.Relational` and
   `Sylin.Koan.Data.Relational.Abstractions` are in the shipped inventory. Determine what they provide
   and reuse rather than reimplement. If they cannot serve MySQL's dialect, record that as a deviation
   and proceed within your own connector — **do not modify them**.
4. A container runtime is available and a MySQL or MariaDB image can start.

## Facts as of authoring (2026-08-19 — verify)

- Record connectors live at `src/Connectors/Data/<Name>/`, package
  `Sylin.Koan.Data.Connector.<Name>`.
- Shipped record connectors: Cockroach, Couchbase, ElasticSearch, InMemory, Json, Mongo, OpenSearch,
  Postgres, Redis, SqlServer, Sqlite.
- Record-plane registration is by a file named `<Adapter>AodbConformanceSpec.cs` — **no `Vector`
  infix**. Existing examples: `PostgresAodbConformanceSpec.cs`, `SqlServerAodbConformanceSpec.cs`.
- Current concrete record-provider suites live under `tests/Suites/Data/Connector.<Name>/`; the shared
  kit alone lives under `tests/Suites/Data/AdapterSurface/`.

## Required artifacts — exact names

Adapter name `MySql` everywhere — that casing exactly, not `MySQL` or `Mysql`.

- Connector: `src/Connectors/Data/MySql/`, project `Koan.Data.Connector.MySql.csproj`, package
  `Sylin.Koan.Data.Connector.MySql`. Mirror the Postgres connector's layout.
- Conformance: `tests/Suites/Data/Connector.MySql/Koan.Data.Connector.MySql.Tests/` containing exactly
  `Specs/MySqlAodbConformanceSpec.cs`, deriving from `AodbConformanceSpecsBase` and overriding only what
  that base declares abstract. Read the base to find out what that is; do not assume it matches the
  vector base.
- Adapter key: `mysql`, lowercase.
- Add both projects to `Koan.sln`.
- Create the connector's project-local `version.json` through BOOTSTRAP's new-project exception with
  compatibility line `1.0`, `versionHeightOffset` `0`, and the standard path filters.

**MariaDB compatibility:** target MySQL. If MariaDB works unchanged, say so in the README. Do not add
a second connector, a compatibility flag, or dialect branching for it in this task.

## Oracle

```powershell
pwsh scripts/forge-verify.ps1 -Adapter MySql -Plane record
```

**Must exit `0`.** Exit `2` (skipped) is a failure. Exit `3` means the spec filename is wrong — check
that you did *not* include `Vector` in it.

## Discoverability

1. Row in the **Data** store table of `docs/reference/capability-map.md`, matching existing rows.
2. Add the package to every recipe enumerating entity store choices:

```powershell
Select-String -Path docs/recipes/*.md -Pattern 'Data.Connector'
pwsh scripts/build-recipe-index.ps1
```

`docs/recipes/store-and-expose.md` enumerates stores as of authoring; verify with the command rather
than trusting that.

## Acceptance

```powershell
pwsh scripts/forge-verify.ps1 -Adapter MySql -Plane record   # exit 0
dotnet build Koan.sln                                        # succeeds, no new warnings
pwsh scripts/skills-verify.ps1 -Structure                    # exit 0
pwsh scripts/docs-lint.ps1                                   # Errors: 0
pwsh scripts/build-recipe-index.ps1 -Check                   # exit 0
pwsh scripts/build-connector-matrix.ps1                       # regenerate, then -Check must exit 0
dotnet run --project tools/Koan.Packaging -- quality `
  --output docs/reference/package-quality.json `
  --markdown docs/reference/package-quality.md      # regenerate; commit the result
```

One commit, `feat(connector): mysql on the record plane`, then the ledger entry.

## Out of scope

- Do not modify `Sylin.Koan.Data.Relational`, its abstractions, or any existing connector.
- Do not add a separate MariaDB connector.
- Do not implement vector support. MySQL vector work is not part of this initiative.
- Do not add, rename, or weaken any conformance spec. The record kit is stricter than the vector kit;
  if a spec fails because MySQL genuinely cannot satisfy it, that is a **finding to record as a
  deviation**, not a spec to change.
- Do not touch `product/claims.json` or describe the connector as assessed.
- Do not update any `AGENTS.md`.
