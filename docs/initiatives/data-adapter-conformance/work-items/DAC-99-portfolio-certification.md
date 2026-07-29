---
type: SPEC
domain: data
title: "DAC-99 Independently Certify the Data Adapter Portfolio"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: independent final portfolio certification prompt
---

# DAC-99 — Independently certify the Data adapter portfolio

| Field | Value |
|---|---|
| Phase / kind | closure / independent certification |
| Depends on | DAC-90 |
| Primer scope | complete current primer including Vector annex, manifests, and portfolio packet index |
| Production writes | forbidden; only final evidence, PROGRESS, and NOW may be updated |
| Owner | Independent certifier |

## Meaningful outcome

A fresh reviewer can independently prove that Koan.Data keeps every advertised adapter promise and that the reusable
primer/Forge workflow is sufficient to evaluate the next adapter without implementation history.

## Independence preflight

1. Use a reviewer who did not implement DAC-04–DAC-90. Reproduce the sealed checkpoint in a disposable clean worktree
   from its authorized commit or base-commit + patch + source manifest; record toolchain, operating system, provider
   digests, credentials posture, and performance runner identity.
2. Re-derive the adapter/family/public-surface roster from source and packages; compare it to DAC-90 without trusting
   its list. Any mismatch is RED and returns to DAC-90.
3. Recompute profile applicability and claim/evidence links from machine-readable declarations. Do not accept manually
   curated green summaries as proof.

## Execute

1. Validate all packet schemas, hashes/provenance, primer/profile IDs, owner assignments, dependency/impact indexes,
   declines, and maturity claims. Any stale consumer is RED even if its historical verdict was green.
2. Run the complete strict Forge matrix: Framework/family, dockerless, both gold providers, networked fleet, faults,
   lifecycle/security, restart/soak, performance, Vector, and heavy-provider lanes. No required LIVE cell may skip.
3. Run full solution build/test and docs/product-surface validation appropriate to the pinned repository. Investigate
   every failure; no production or public-truth repair is allowed in this card.
4. Re-run representative native plans/requests and cross-gold/vector differential corpora. Sample evidence back to raw
   provider output and verify no secrets or private data entered packets.
5. Have the reviewer execute the author workflow against a deliberately incomplete fixture adapter. It must expose a
   false claim, a missing decline, a policy bypass, and absent LIVE evidence without extra tribal knowledge.
6. Publish the final report with PASS/RED/DEFER counts, roster, commands, environment, exceptions, and exact blockers.

## Verification

- PASS requires zero RED/DEFER rows for every shipping adapter and every required CI lane.
- Mutation checks prove false claims, skipped providers, stale docs, changed evidence, and policy bypasses all fail.
- A second reviewer checks the final report and evidence index without relying on the implementing agent's narrative.

## Definition of done

- [ ] The independently derived roster equals the shipped/public roster.
- [ ] Every advertised claim has current executable evidence and every decline fails closed.
- [ ] Every withdrawal, downgrade, Target/Declined choice, and non-shipping disposition resolves to human authority and
  a separately pinned identity.
- [ ] All required build, conformance, LIVE, fault, lifecycle, performance, and docs lanes pass.
- [ ] The final report and restartable adapter-author workflow are complete and privacy-clean.
- [ ] `PROGRESS.md` records portfolio PASS and `NOW.md` records closure/maintenance ownership.

## Stop conditions

Any roster mismatch, production/public-truth change, skipped required provider, stale or unverifiable packet, privacy
failure, or RED/DEFER shipping row stops certification and creates a bounded return card.
