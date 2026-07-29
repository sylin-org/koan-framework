---
type: SPEC
domain: data
title: "DAC-24 Review the Complete MongoDB Replacement"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: MongoDB atomic replacement, architecture, behavior, and absence gate
---

# DAC-24 — Review the complete MongoDB replacement

| Field | Value |
|---|---|
| Phase / kind | gold / replacement review |
| Depends on | DAC-21 |
| Unlocks | DAC-23 with DAC-13 |
| Primer scope | ratified MongoDB contract plus complete replacement evidence |
| Production writes | forbidden; `evidence/mongodb/**` review artifacts only |
| Owner | Independent replacement review |

## Meaningful outcome

The MongoDB change is demonstrably a complete, lean, atomic replacement with honest topology behavior and no hidden
legacy route.

## Required work

1. Reproduce the DAC-21 change against DAC-15's common base and resolve every retirement entry to absence.
2. Verify exactly one project/package, activation/factory path, registration, repository/native execution path per
   operation, claim source, and adapter-test authority. Search compile items, DI/reflection discovery, generated output,
   aliases, docs, fixtures, and unreachable code for alternate paths.
3. Review every moving part against its declared contract/shared-mechanics/hot-path reason. Reject duplicate Document
   ownership, speculative extension points, warm-path discovery, unbounded pool/cache/background state, and abstraction
   without measured value.
4. Run the full real MongoDB topology/permission/behavior/native/fault/lifecycle/soak/performance matrix. A missed valid
   public behavior becomes a contract-linked failing test for the new implementation, never a reason to restore old
   code.
5. Review for copied or mechanically transformed internal structure and for `Legacy`/`V2`/`Compat`, feature-flag,
   bridge, fallback, and shadow-registration patterns.
6. Seal the complete deletion+new-source manifest, architecture verdict, behavior verdict, and reproducible identity
   for DAC-23. This card does not repair production failures.

## Verification

- Omitting a retirement entry or seeding a compile item, registration, fixture, helper, bridge, or shadow path fails.
- Every new-source path and moving-part reason resolves.
- Required MongoDB topology, native trace, fault, resource, soak, and provider-relative baseline evidence reproduces.
- Focused build/tests and `git diff --check` pass on the atomic change.

## Definition of done

- [ ] One complete base-relative MongoDB replacement is reproducible and atomic.
- [ ] Legacy/dead-path absence, architecture, native behavior, topology truth, and performance reviews are green.
- [ ] Every valid public behavior is covered or returned as a black-box failure for DAC-21 re-entry.
- [ ] No compatibility, bridge, fallback, or unexplained moving part remains.

## Stop conditions

An incomplete inventory, production edit, copied structure, unjustified moving part, hidden alternate path, missing
required topology/role, or behavioral/native/performance failure blocks DAC-23.
