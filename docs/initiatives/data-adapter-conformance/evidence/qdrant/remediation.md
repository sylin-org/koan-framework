---
type: REFERENCE
domain: data
title: "Qdrant Remediation Ledger"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green-strict-deferred
  scope: DAC-53 empty-root replacement record
---
# Qdrant remediation

| Remediation | Disposition | Owner | Invalidated consumers | Re-entry proof |
|---|---|---|---|---|
| First-write/schema options competed with `VectorSpacePlan` | deleted; plan is sole shape owner | Qdrant adapter | old options/docs/tests | V-01, V-02, V-09, V-20 |
| Removed `/points/search` and legacy DTO path | deleted; current Query API only | Qdrant client/repository | old repository | V-07–V-10 live |
| Collection-dropping clear | replaced with scoped delete-by-filter | Qdrant repository | lifecycle callers | V-20, V-21 |
| Lossy metadata conversion | split lossless neutral storage from native filter projection | Qdrant repository/filter | metadata/filter callers | V-06, V-13 |
| Negative numeric ID collapse and unscoped identity | replaced with typed deterministic projection and scoped IDs | Qdrant repository | signed/scoped keys | V-03–V-05, V-21, G-09 |
| Fabricated batch atomicity/counts | replaced with ordered per-item outcomes and explicit non-atomic truth | Qdrant repository | bulk callers | V-17, V-18 |
| Quantization/on-disk/wait/field-name/collection knobs | deleted from public options | package surface | configuration consumers | API build, README, V-11 |
| Case-folding physical-name declaration | corrected to Qdrant's case-preserving capability | factory/naming | routed mixed-case sources | all live cells, G-09/database |
| Restart fixture reused random endpoint and stale client pool | stable REST bind and bounded fresh readiness probe | test fixture | V-23 | V-23 live in two seconds |
| Versioned strict packet | deferred; do not synthesize locally | shared conformance control plane | strict Forge status | all rows PASS, final DEFERRED |
