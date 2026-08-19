---
type: GUIDE
domain: framework
title: "Connector fleet"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-08-19
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-19
  status: verified
  scope: task roster, oracle commands, and fenced scope
---

# Connector fleet

**Mission.** Make stores an application already operates do more, so adopting a Koan capability stops
requiring a new service.

Read [ARCH-0127](../../decisions/ARCH-0127-connector-fleet-strategy.md) first. It is normative. This
file is the execution surface and deliberately restates none of its reasoning.

## Why these four

Koan ships Postgres, Mongo, and Redis as record stores. All three have first-class vector modes that
Koan does not expose, so a developer already running one of them must adopt a *second* service to get
semantic search. Three of the four tasks close that gap. The fourth fills the largest absence in the
record plane.

Every task is provable by a command that already exists. That is the admission criterion, not a
preference — see the fenced scope below.

## Roadmap

Dependency-ordered. Each task lands independently and leaves a green tree, so stopping after any of
them is a coherent outcome.

| # | Task | Plane | Oracle |
|---|---|---|---|
| T1 | [pgvector](tasks/T1-pgvector.md) | vector | `pwsh scripts/forge-verify.ps1 -Adapter PgVector -Plane vector` |
| T2 | [Redis vector](tasks/T2-redis-vector.md) | vector | `pwsh scripts/forge-verify.ps1 -Adapter RedisVector -Plane vector` |
| T3 | [MySQL / MariaDB](tasks/T3-mysql.md) | record | `pwsh scripts/forge-verify.ps1 -Adapter MySql -Plane record` |
| T4 | [Mongo Atlas Vector](tasks/T4-mongo-atlas-vector.md) | vector | `pwsh scripts/forge-verify.ps1 -Adapter MongoAtlasVector -Plane vector` |

T1 is first because it is the smallest, has the widest container support, and sets the pattern the
other vector tasks follow. T4 is last because its container story is the least certain and it is the
task most likely to end BLOCKED.

Progress lives in exactly one place: [LEDGER.md](LEDGER.md). Ground rules and the failure protocol
live in [BOOTSTRAP.md](BOOTSTRAP.md). Read that before starting any task.

## Acceptance contract

A task is complete when **all** of the following hold. Nothing here is a matter of judgement.

1. Its oracle command exits `0`. Exit `2` means specs were skipped and is **not** acceptance.
2. `pwsh scripts/skills-verify.ps1 -Structure` exits `0`.
3. `pwsh scripts/docs-lint.ps1` reports `Errors: 0`.
4. `dotnet build Koan.sln` succeeds with no new warnings.
5. The connector is discoverable: a row in `docs/reference/capability-map.md`, and its package added to
   the ingredient list of every recipe under `docs/recipes/` that it belongs to, with
   `pwsh scripts/build-recipe-index.ps1` re-run.
6. The package inventory is regenerated and committed — a new package changes the MSBuild graph, and a
   stale inventory makes the capability map unverifiable:

   ```powershell
   dotnet run --project tools/Koan.Packaging -- quality `
     --output docs/reference/package-quality.json `
     --markdown docs/reference/package-quality.md
   ```

7. The connector matrix is regenerated and committed — `pwsh scripts/build-connector-matrix.ps1` —
   so the new provider appears on the one page that answers "does Koan support X?".
8. The ledger entry for the task is written, including any deviations.

The initiative is complete when T1–T4 are each Done or BLOCKED with a recorded reason, and no task is
left in progress.

## Fenced scope — not in this initiative

| Excluded | Why |
|---|---|
| Hosted AI connectors (OpenAI-protocol, Anthropic, Gemini) | No AI conformance kit exists, so no oracle exists; and ARCH-0127 gates them behind an egress-governance decision that is frontier work. |
| Building a new conformance kit | Designing an oracle is the work that must not be delegated. A missing kit is a STOP condition. |
| Promoting `Storage.Connector.S3` or `Cache.Adapter.Redis` | Product-claim work under ARCH-0120, not connector construction. |
| Any product-claim or maturity change | Merging a connector grants nothing. The claim ledger owns maturity. |

## Archival

This initiative moves to `docs/archive/` when every task is Done or BLOCKED and the outcome is
reflected in the capability map. A BLOCKED task is a completed initiative outcome, not a failure —
record why, and let a future decision reopen it.
