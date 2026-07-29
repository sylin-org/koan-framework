---
type: SPEC
domain: data
title: "DAC-13 Review the Complete SQLite Replacement"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: SQLite atomic replacement, architecture, behavior, and absence gate
---

# DAC-13 — Review the complete SQLite replacement

| Field | Value |
|---|---|
| Phase / kind | gold / replacement review |
| Depends on | DAC-11 |
| Unlocks | DAC-23 with DAC-24 |
| Primer scope | ratified SQLite contract plus complete replacement evidence |
| Production writes | forbidden; `evidence/sqlite/**` review artifacts only |
| Owner | Independent replacement review |

## Meaningful outcome

The SQLite change is demonstrably a complete, lean, atomic replacement rather than an incremental cleanup with hidden
legacy paths.

## Required work

1. Reproduce the DAC-11 change against DAC-15's common base and resolve every retirement entry to absence.
2. Verify exactly one project/package, activation/factory path, registration, repository/native execution path per
   operation, claim source, and adapter-test authority. Search compile items, DI/reflection discovery, generated output,
   aliases, docs, fixtures, and unreachable code for alternate paths.
3. Review every moving part against its declared contract/shared-mechanics/hot-path reason. Reject duplicate ownership,
   speculative extension points, warm-path discovery, unbounded state, and abstraction without measured value.
4. Run the full real SQLite behavioral/native/fault/lifecycle/performance matrix. Compare required public behavior and
   black-box cases; a missed valid behavior becomes a contract-linked failing test for the new implementation, never a
   reason to restore old code.
5. Review for copied or mechanically transformed internal structure and for `Legacy`/`V2`/`Compat`, feature-flag,
   bridge, fallback, and shadow-registration patterns.
6. Seal the complete deletion+new-source manifest, architecture verdict, behavior verdict, and reproducible identity
   for DAC-23. This card does not repair production failures.

## Verification

- Omitting a retirement entry or seeding a compile item, registration, fixture, helper, bridge, or shadow path fails.
- Every new-source path and moving-part reason resolves.
- Required SQLite native plans, fault cases, and provider-relative baselines reproduce.
- Focused build/tests and `git diff --check` pass on the atomic change.

## Definition of done

- [ ] One complete base-relative SQLite replacement is reproducible and atomic.
- [ ] Legacy/dead-path absence, architecture, native behavior, and performance reviews are green.
- [ ] Every valid public behavior is covered or returned as a black-box failure for DAC-11 re-entry.
- [ ] No compatibility, bridge, fallback, or unexplained moving part remains.

## Stop conditions

An incomplete inventory, production edit, copied structure, unjustified moving part, hidden alternate path, or
behavioral/native/performance failure blocks DAC-23.
