---
type: SPEC
domain: framework
title: "R07-03 - Automate Breaking Package Lineage"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: in-progress
  scope: durable reverse-dependent package closure before the Lifecycle 0.18 break
---

# R07-03 — Automate breaking package lineage

- Tranche: `T6 — semantic capability ring`
- Status: `in-progress`
- Depends on: R07-02, ARCH-0085, and ARCH-0110
- Unlocks: the clean Lifecycle 0.18 break and every later independent breaking package wave
- Owner: Koan.Packaging and the serialized `dev` release workflow

## Meaningful outcome

A maintainer expresses compatibility intent only by changing source and the owning package's
`version.json`. Advancing `dev` then automatically:

1. finds every package affected by a breaking-tier root;
2. gives unchanged reverse dependents fresh immutable identities;
3. builds, tests, packs, and proves the exact resulting Git tree; and
4. resumes safely when an earlier publication stopped partway through.

There is no package checklist, version spreadsheet, patch calculation, or operator-selected release
set. Ordinary leaf changes remain independent.

## Decisions

### DECIDED

- Evaluated MSBuild projects and `ProjectReference`s are the only package graph authority.
- One `PackageGraph` owns dependency resolution, topological order, and transitive reverse closure.
- One `ReleaseLineageCompiler` owns the durable Git projection used to mint otherwise-unchanged
  dependent identities.
- `version.json` remains the human compatibility signal. Before 1.0, a minor advance is breaking;
  from 1.0 onward, a major advance is breaking.
- The protected workflow maintains one serialized `automation/package-lineage-dev` branch. Each
  accepted source advancement becomes one lineage commit containing the source merge, deterministic
  markers, and inspectable lineage state.
- A generated package marker is written only when a closure member would otherwise retain its prior
  NBGV identity. The marker names the source commit and breaking roots that required the rebuild.
- Manifests distinguish `SourceCommit` from `VersionCommit`; version comparison starts at
  `PreviousVersionCommit`. Package metadata, resumable state, packing, and release evidence bind to
  `VersionCommit`.
- Existing registry reconciliation remains the recovery mechanism for identities that were compiled
  but not published by an earlier run.
- Package deletion/rename, reserved marker collisions, non-forward source history, graph cycles, and
  incomplete closure fail before packing or publication.
- The initial implementation changes no application/runtime API and does not publish, push, tag, or
  release from a developer checkout.

### DEFAULT

- One serialized workflow job favors correctness and inspectability over maximum release throughput.
- Source-event identity remains visible in summaries alongside the exact version commit.

### OPEN

- Package deletion and rename may gain an explicit migration protocol later. They are unsupported in
  the first automatic-lineage contract rather than inferred from incomplete after-tree evidence.

## Architecture guardrails

```text
dev source advancement
        |
serialized lineage compiler
        |
version.json breaking roots -> PackageGraph reverse closure
        |                         |
source-changed members       deterministic markers for unchanged members
        \_________________________/
                    |
             exact VersionCommit
                    |
        plan -> build/test -> pack/prove -> publish
```

- Git owns change intent and durable identity; the registry is reconciliation evidence, not a version
  allocator.
- The lineage compiler mutates only its dedicated local branch and reserved marker/state files.
- No production package graph is parsed from XML, inferred from names, or maintained in a second list.
- No later workflow run may calculate or mint package identities before earlier lineage mutation has
  completed.
- Every pack/metadata check uses the exact version commit, never the source event by implication.

## Red/green plan

1. Add failing `PackageGraph` tests for dependency order, complete reverse closure, independence, and
   cycle rejection.
2. Extract graph behavior from `ReleasePlanner` without changing existing release selection.
3. Add failing compiler-decision tests for breaking tiers, marker selection, deterministic causes,
   deletion/rename, reserved collisions, and non-forward history.
4. Implement the Git-native lineage compiler and schema-versioned state/marker artifacts.
5. Split source, previous-version, and version commit semantics across manifest, planner, pack
   metadata, resumable state, console reporting, and release evidence.
6. Serialize the protected workflow before lineage compilation and make it build/test/pack/publish the
   exact version commit.
7. Prove two source waves in a temporary Git repository: a breaking root mints its complete reverse
   closure, an unrelated leaf stays unchanged, and a later run can reconcile the same durable
   identities.
8. Run packaging tests, a real repository offline plan, build/tests, workflow structural checks,
   strict docs, diff, and privacy gates before closing the child.

## Acceptance

- A synthetic pre-1.0 minor bump automatically gives the root and every transitive reverse dependent
  a fresh version.
- A source-untouched dependent gets a deterministic Git marker; a source-touched dependent is not
  bumped twice.
- An unrelated package retains its prior identity.
- Omitting a required closure member is impossible through the public planner path and detected by an
  independent closure assertion before publish.
- Re-running or recovering a source event uses the same durable version lineage and registry
  reconciliation; it never reuses a published identity for different bits.
- The manifest and packed nuspec agree on `VersionCommit`, while `SourceCommit` remains available for
  audit.
- Unsupported history/package-shape changes fail with corrective package/path/source evidence.
- No package is published, no remote ref is changed, and no real release is created by the local proof.

