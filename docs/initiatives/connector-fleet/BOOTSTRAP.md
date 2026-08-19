---
type: GUIDE
domain: framework
title: "Connector fleet — bootstrap"
audience: [ai-agents, maintainers]
status: current
last_updated: 2026-08-19
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-19
  status: verified
  scope: ground rules, authority order, environment facts, and failure protocol
---

# Bootstrap — read this before any task

Assume you have no memory of how this initiative came to exist and no conversation preceding this
file. Everything you need is here or is cited from here.

## Ground rules

1. **One task, one commit.** Do not begin a task before the previous one is committed or recorded
   BLOCKED.
2. **Re-read the tree.** Every fact in a task prompt is labelled as-of-authoring. Verify it against the
   working tree before relying on it. Where they disagree, the tree wins and the disagreement is a
   deviation to record.
3. **Never derive an expected result.** Expected outcomes are pinned in the task prompt. If something
   you must assert is not pinned, that is a STOP condition — do not invent the expectation, because an
   expectation you derive will validate your own bug.
4. **Copy the closest existing pattern.** Every task has a named reference adapter already in the tree.
   Match its structure, naming, and file layout rather than designing a new one.
5. **A green run is not acceptance.** Read the acceptance contract in [README.md](README.md). Exit code
   `2` from the oracle means specs were *skipped* and is a failure for our purposes.
6. **Do not improve things you were not asked to change.** Unrelated cleanups make a task
   unreviewable.

## Authority order

When two sources conflict, the higher one wins, and the conflict is a deviation to record:

1. [ARCH-0127](../../decisions/ARCH-0127-connector-fleet-strategy.md) — normative for what belongs in
   the fleet and why.
2. This bootstrap.
3. The task prompt.
4. The named reference adapter in the tree.
5. `CLAUDE.md` at the repository root — framework-wide law.
6. Everything else, including any other documentation.

The `Provenance` table in ARCH-0127 records questions that are already settled. Do not reopen them.

## Environment facts (as of 2026-08-19 — verify)

- Branch `dev`. Do not work on `main`; merging to `main` publishes packages.
- .NET SDK 10.0.302, solution `Koan.sln`.
- Shell is PowerShell 7 (`pwsh`). All commands below are literal.
- Conformance kits, which you must not modify:
  - record — `tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs`
  - vector — `tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/VectorAodbConformanceSpecsBase.cs`
- `scripts/forge-verify.ps1` discovers a target by **filename**: a file named
  `<Adapter>VectorAodbConformanceSpec.cs` registers `<Adapter>` on the vector plane;
  `<Adapter>AodbConformanceSpec.cs` registers it on the record plane. The project it runs is the
  nearest `.csproj` walking up from that file. Naming the file correctly *is* the registration — there
  is no list to edit.
- Oracle exit codes: `0` all passed · `1` a test failed · `2` one or more skipped · `3` runner error.
- Package versions come from NBGV. Never hand-write a version.

## Task sequence

Strictly in order: **T1 → T2 → T3 → T4**. Each leaves a green tree. If a task ends BLOCKED, continue to
the next one.

## Per-task procedure

1. Open the task prompt. Read it fully before editing anything.
2. Check its **STOP preconditions**. If any fails, record BLOCKED in the ledger and move on. Do not
   improvise around a failed precondition.
3. Verify the as-of-authoring facts against the tree. Record differences as deviations.
4. Implement, copying the named reference adapter's structure.
5. Run the task's oracle command. It must exit `0`.
6. Run the full acceptance contract from [README.md](README.md).
7. Regenerate the package inventory, because you added a package to the graph:

   ```powershell
   dotnet run --project tools/Koan.Packaging -- quality `
     --output docs/reference/package-quality.json `
     --markdown docs/reference/package-quality.md
   ```

   Commit the regenerated files with your work. If the run fails, that is a STOP condition — a stale
   inventory makes the capability map unverifiable.

8. Commit — one commit, message `feat(connector): <what it adds>`.
9. Write the ledger entry, including numbered deviations. An empty deviation list is a valid entry.

## Failure protocol

When a task cannot complete:

1. **Revert the working tree** to the last commit. Do not leave partial work.
2. **Record BLOCKED** in [LEDGER.md](LEDGER.md) with: which step failed, the exact command and its
   output, what you tried, and what you believe is required to unblock it.
3. **Continue to the next task.** Do not halt the initiative, and do not attempt a task out of order to
   compensate.

Three consecutive BLOCKED tasks means something is wrong with the initiative rather than the tasks.
Stop and say so in the ledger.

Never make a failing check pass by weakening the check. Skipping a spec, loosening an assertion,
editing a kit, or excluding a test from a run is a worse outcome than a recorded BLOCKED.

## NEVER touch

Each entry names its single sanctioned exception, or states that there is none.

| Never | Sanctioned exception |
|---|---|
| `AodbConformanceSpecsBase.cs`, `VectorAodbConformanceSpecsBase.cs`, `tests/Suites/_shared/CapabilityConformanceGate.cs` | **None.** A kit that seems to need changing is a STOP condition. |
| `scripts/forge-verify.ps1` | **None.** Registration is by filename; there is nothing to edit. |
| `scripts/skills-verify.ps1`, `scripts/docs-lint.ps1`, `scripts/build-recipe-index.ps1` | **None.** Run them; do not modify them. |
| `product/claims.json` and any maturity or product-claim wording | **None.** ARCH-0120 owns maturity. |
| `docs/recipes/index.md` | **None** by hand — it is generated. Regenerate with `pwsh scripts/build-recipe-index.ps1`. |
| Any `version.json`, or any hand-written package version | **None.** NBGV owns versions. |
| `AGENTS.md` at the repository root or in `templates/` | **None.** By DX-0050, adding a capability must edit no bootstrap. Needing to is a design smell — record it as a deviation. |
| `src/Koan.Core/**` | **None.** |
| `Directory.Packages.props` | **Permitted, narrowly.** Central package management is on, so a new connector's client library needs a `PackageVersion` entry here. Add only the entries your task's connector requires; change no existing version. |
| `docs/reference/package-quality.json` and `package-quality.md` | **None** by hand — they are generated from the MSBuild project graph. Regenerate (see the per-task procedure). |
| An existing connector's runtime under `src/Connectors/**` | **None.** Read them as reference; do not edit them. |
| Hosted AI connectors of any kind | **None.** Fenced by ARCH-0127. |
| `main` branch | **None.** Work on `dev`. |

## Completion criteria

The initiative is finished when the ledger shows every task Done or BLOCKED, no task is in progress,
and the acceptance contract holds for each Done task. Then say so in the ledger and stop. Do not add
tasks; a new connector is a new decision, not an extension of this one.
