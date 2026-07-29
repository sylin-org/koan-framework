---
type: ARCHITECTURE
domain: data
title: "DAC-50 Build the Vector Conformance Control Plane"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: passed
  scope: Vector TestKit, Forge, claim projection, and strict-runner implementation prompt
---

# DAC-50 — Build the Vector conformance control plane

| Field | Value |
|---|---|
| Phase / kind | vector / conformance-tooling remediation |
| Depends on | DAC-49 |
| Unlocks | DAC-04 |
| Primer scope | pinned Source Core and human-ratified Vector annex |
| Production writes | only `src/Koan.Data.Vector/**` conformance declarations, `src/Koan.Testing/**`, `tests/Suites/Data/VectorAdapterSurface/**`, `scripts/forge-verify.ps1`, and initiative evidence/docs; no provider adapters or public semantic changes |
| Owner | Framework(Vector conformance projection) |

## Meaningful outcome

Every ratified Vector claim mechanically selects the right executable cells and evidence without creating a second
semantic authority or pretending similarity search is ordinary Entity query.

## Required work

1. Use the production-code `explore` workflow. Re-pin DAC-49 and inspect `Koan.Data.Vector`, `Vector<TEntity>`, the
   VectorAdapterSurface TestKit, Forge, runtime facts, and the shared claim/evidence schema from DAC-03.
2. Project the primer's exact Source Core, conditional source profiles, and ratified Vector-annex IDs into the existing
   one-claim-truth system. Do not copy or rename semantic obligations into a Vector-owned catalog.
3. Extend the Vector TestKit so each claim selects its tests, negative paths, evidence kinds, facts, packet rows, and
   public capability projection from the same declaration.
4. Add strict certification behavior for unavailable factories, native libraries, credentials, containers, required
   architectures, or skipped LIVE cells. These are INFRASTRUCTURE/DEFER with distinct corrections and never produce PASS.
5. Add deterministic oracle, native-request/plan, settling, lifecycle, fault, isolation, cancellation, and performance
   modules required by the annex. Provider-specific fixtures lower only the native seam.
6. Version the projection with the DAC-49 primer fingerprint and reject stale evidence or incompatible provider
   manifests. Preserve DAC-03 packet/runner conventions rather than forking the toolchain.
7. Keep every current provider row RED/DEFER until its own read-only evaluation and independent certification runs.
8. Re-run DAC-03 base integrity and schema compatibility before handing one stable runner identity to DAC-04. No later
   provider card may alter shared Forge/profile semantics; such a finding returns to this Framework owner and triggers
   impact invalidation.

## Verification

- Catalog integrity proves every ratified primer ID has complete claim-to-test/evidence mappings and no duplicate
  semantic authority.
- Mutation tests prove strict mode fails for an unavailable provider and for a false search/filter/dimension claim.
- Existing vector suites compile against the projection without provider-specific semantics entering Framework.

## Definition of done

- [x] Every DAC-49 cell maps to executable tests/evidence or an explicit provider seam.
- [x] Source Core remains shared with Entity/Source Integration rather than reimplemented for Vector.
- [x] Forge strict mode cannot turn absent LIVE evidence into green certification.
- [x] No provider has been certified or changed by this tooling card.

## Stop conditions

Stop if DAC-49 is not human-ratified, the primer fingerprint changes, a second catalog emerges, a public semantic must
change, or provider production code is needed to make the control plane pass.
