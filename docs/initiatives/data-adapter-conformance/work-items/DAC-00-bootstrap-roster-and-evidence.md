---
type: SPEC
domain: data
title: "DAC-00 Bootstrap the Adapter Roster and Evidence System"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: bootstrap prompt, current reconnaissance anchors, and exit gate
---

# DAC-00 — Bootstrap the adapter roster and evidence system

| Field | Value |
|---|---|
| Phase / kind | foundation / audit-tooling |
| Depends on | — |
| Unlocks | DAC-01 |
| Primer scope | §10.1, §10.2, §10.7 |
| Production writes | forbidden |
| Owner | Initiative tooling |

## Meaningful outcome

The epic starts from a reproducible inventory rather than a remembered adapter list or inherited support table.

## Required work

1. Pin base commit, exact worktree state, primer hash/status, SDK, OS/architecture, Docker availability, and date.
2. Dynamically discover Data adapter packages, factories/modules, family substrates, dedicated tests, provider fixtures,
   runtime capability declarations, README/TECHNICAL files, and product claims.
3. Classify discovered adapters by conformance kind without assuming directory names are correct. Elasticsearch and
   OpenSearch are currently Vector-only; verify rather than copy that fact.
4. Create `evidence/portfolio/roster.json` and a safe human summary. Record cache/AI adjacency without adding them.
5. Create the empty framework and per-adapter §10.7 packet skeletons from `evidence/README.md`, including consumed
   owner/path/tool/profile dependency records for later impact invalidation. Add the conditional whole-adapter
   replacement schemas for lessons, retirement, empty-root/new-source proof, moving-part justification, and absence.
7. Add an initiative integrity check that verifies unique card IDs, one progress row per static card, known and acyclic
   dependencies, at most one in-progress row before DAC-30, valid primer acceptance-ID references, and resolving local
   links. Keep this initiative-specific; do not invent a second general build system.
8. Run the current Forge in report mode and record its actual cells and limitations. A skipped provider is recorded as
   inconclusive, not green.
9. Implement the evidence identity protocol from `evidence/README.md`: clean-commit proof or sealed initiative patch,
   changed/untracked path hashes, resultant source fingerprint, and disposable-worktree reproduction. Prove unrelated
   dirty files are excluded and no commit is created without explicit user authorization.
10. Add a fail-closed replacement validator: require an empty implementation start, complete retirement, unique
    compile/registration entries, exactly one execution path, no shadow path, and one necessary reason per moving part.

## Evidence anchors

- `src/Connectors/Data/**/**.csproj`
- `tests/Suites/Data/**/**.csproj`
- `src/Koan.Data.Abstractions/Capabilities/DataCaps.cs`
- `scripts/forge-verify.ps1`
- `product/claims.json`
- the current AdapterSurface and VectorAdapterSurface TestKits

## Verification

- Run the initiative integrity check twice and prove deterministic output.
- Compare dynamic project discovery to solution membership and product claims.
- Run focused docs lint on this initiative and `git diff --check`.
- Record exact Forge command/output without treating the present five/six broad AODB facts as primer coverage.

## Definition of done

- [ ] The roster is generated from repository facts and identifies every current adapter/family/test/claim edge.
- [ ] Packet skeletons exist without duplicating a human-maintained capability catalog.
- [ ] Integrity checks fail on a temporary duplicate ID, bad dependency, and unknown primer ID, then pass restored.
- [ ] Replacement checks fail on a non-empty start, incomplete retirement, duplicate selected path, unexplained moving
      part, multiple execution paths, and shadow registration.
- [ ] A dirty authorized initiative checkpoint reproduces exactly in a disposable clean worktree from its sealed identity.
- [ ] PROGRESS and NOW name DAC-01 as the exact next action.

## Stop conditions

Stop if adapter discovery is ambiguous, a shipped connector has no identifiable factory/module, or the baseline cannot
build far enough to distinguish pre-existing failures from initiative work.
