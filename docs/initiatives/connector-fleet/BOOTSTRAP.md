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
3. **Pin expected results before implementation.** Expected outcomes come from the task prompt. When
   the working tree has gained a conformance proof surface since the prompt was authored, repair the
   initiative requirements first: record the complete expected-outcome profile from the current kit
   and named reference, commit that requirement repair, and only then implement. Never tune an
   expectation after seeing a provider fail it.
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

## Vector annex proof profile

The current vector kit inherits 24 provider proof-seam facts in addition to the AODB isolation cells.
T1, T2, and T4 are authorized and required to override `ProveVectorAnnexCellAsync` while leaving every
`[Fact]` inherited and unchanged. The override dispatches every ID below to private provider-specific
proof helpers. An unknown ID delegates to the base so a future kit addition remains a loud skip.

These outcomes are pinned before connector implementation:

| ID | Required outcome |
|---|---|
| V-01 | Reject a wrong embedding dimension before mutation and reject an existing native space with an incompatible dimension or metric. |
| V-02 | Reject empty, wrong-sized, and non-finite embeddings before provider mutation; reject zero-norm embeddings when the selected metric requires it (Cosine), while preserving the valid Euclidean zero-vector case used by V-08. |
| V-03 | Re-saving one identity replaces its vector and metadata without creating a duplicate. |
| V-04 | Delete returns `true` for an existing point, `false` for a missing point, and propagates cancellation or provider failure. |
| V-05 | Get-many preserves input order, duplicate positions, and `null` slots for missing identities. |
| V-06 | Metadata round-trips through the neutral value algebra without aliasing caller buffers; reserved managed keys fail closed. |
| V-07 | Search is bounded, unique, similarity-descending, and identity-stable at equal scores. |
| V-08 | Cosine, Euclidean, and dot-product scores normalize to finite `[0,1]` similarities whose order is higher-is-closer, and execution reports the requested metric. |
| V-09 | An undeclared or incompatible named space fails before a query reaches the provider. |
| V-10 | Execution truth matches native work: PgVector reports an exact SQL scan, RedisVector an exact FLAT search, and MongoAtlasVector an exact Atlas vector query; none invents candidate counts or continuations. |
| V-11 | Awaited save and delete are immediately visible in `Session` mode without sleeps. |
| V-12 | `Eventual` visibility is rejected because none of these three connectors supplies a bounded `Sync` barrier. |
| V-13 | The connector declares filters and pushes the neutral equality, comparison, set, existence, size, boolean, and negation matrix into the native prefilter; unsupported operators fail closed. |
| V-14 | Hybrid search is not declared and text/semantic weighting throws `NotSupportedException`. |
| V-15 | Multiple named vectors per Entity are not declared and an undeclared space fails closed. |
| V-16 | Native continuation is not declared and a continuation token throws `NotSupportedException`. |
| V-17 | Native bulk save/delete preserves per-item inserted/updated/deleted/missing outcomes, prevalidates the whole request, and reports `NotGuaranteed` atomicity. |
| V-18 | Atomic batch is not declared; invalid mixed input performs no mutation and a valid batch reports `NotGuaranteed`. |
| V-19 | Streaming export is not declared and `ExportAll` throws `NotSupportedException`. |
| V-20 | Ensure, sync, clear, read-only, and external lifecycle policies are honored at the owning boundary. |
| V-21 | Partition/source isolation protects get, search, save, delete, and clear surfaces. |
| V-22 | Cross-store transaction intent fails before vector mutation when atomic coordination is not claimed. |
| V-23 | Cancellation propagates, data survives a real backend restart, and a disposed repository rejects later operations. |
| V-24 | Sixteen local-container save/get/search warm cycles stay below 64 MiB total managed allocation after one warm-up cycle. The default latency budget is 15 seconds; a task may pin a provider-specific budget when the native visibility model has a measured floor. |

The profile is intentionally capability-honest: it proves native support where each provider has it
and proves explicit rejection where the Koan connector does not claim a portable guarantee. Provider
tests may inspect captured native plans or backend schema, but may not add, replace, skip, or weaken a
shared conformance fact.

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

   Then regenerate the connector matrix, which reads that inventory:

   ```powershell
   pwsh scripts/build-connector-matrix.ps1
   ```

   Commit both regenerated outputs with your work. If either run fails, that is a STOP condition —
   a stale inventory makes the capability map unverifiable, and a stale matrix hides your connector
   from the one page that answers "does Koan support X?".

8. Commit — one commit, message `feat(connector): <what it adds>`.
9. Write the ledger entry, including numbered deviations. An empty deviation list is a valid entry.

## Failure protocol

When a task cannot complete:

1. **Revert the working tree** to the last commit. Do not leave partial work.
2. **Record BLOCKED** in [LEDGER.md](LEDGER.md) with: which step failed, the exact command and its
   output, what you tried, and what you believe is required to unblock it.
3. **Continue to the next task.** Do not halt the initiative, and do not attempt a task out of order to
   compensate.

Three consecutive BLOCKED tasks trigger an initiative-authority audit rather than an automatic halt.
If the blockers are contradictions between the task prompts and the current tree, repair the
requirements in one reviewable documentation commit and resume from the first affected task. Stop
only when completion genuinely requires unavailable infrastructure, credentials, external
coordination, or authority that the executor does not have, and say so in the ledger.

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
| `docs/reference/connector-matrix.md` | **None** by hand — it is generated from the package graph. Regenerate with `pwsh scripts/build-connector-matrix.ps1`. Your connector must appear there or nobody asking "does Koan support X?" will find it. |
| Existing `version.json` files, or any hand-written package version | **New packable project only:** copy the closest sibling's compatibility-line file, set `versionHeightOffset` to `0`, and change nothing else. NBGV owns the resulting version. |
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
