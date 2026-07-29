---
type: SPEC
domain: data
title: "DAC-52 Evaluate and Certify the SqliteVec Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: SqliteVec adapter evaluation prompt
---

# DAC-52 — Evaluate and certify the SqliteVec adapter

| Field | Value |
|---|---|
| Phase / kind | vector / audit-certification |
| Depends on | DAC-51 |
| Primer scope | dynamically selected Source Core and ratified Vector manifest |
| Production writes | forbidden |
| Owner | Adapter(SqliteVec); SQLite family findings split explicitly |

## Meaningful outcome

SqliteVec is a portable embedded vector source whose native-extension, file-lifecycle, and similarity guarantees are
observable and fail clearly on unsupported machines.

## Execute

1. Pin managed dependencies and bundled native artifacts for every shipped RID, including win-x64, linux-x64, and
   linux-arm64; create `evidence/sqlitevec/` with hashes and load provenance.
2. Audit extension loading, connection/file ownership, collection/table shape, `vec0` use, dimensions, distance
   metrics, CRUD, filters, `topK`, partitions, source axes, and result mapping.
3. Exercise managed and external lifecycle postures. Prove read-only rejection before file creation, extension load,
   DDL, transaction creation, or any other observable mutation.
4. Capture native query plans and dispatch counts for similarity/filter paths. Compare semantic results with DAC-51;
   provider-native ordering or precision differences must be explicit contract tolerances.
5. Prove unsupported RID, missing/mismatched native binary, locked/corrupt file, cancellation, concurrency, disposal,
   restart persistence, cleanup, cold load, and warm hot-path behavior.
6. RED creates one-owner Framework/SQLite-family/SqliteVec remediation cards and blocks certification.

## Verification

- Run the RID matrix where supported, strict Forge, native-plan assertions, lifecycle negatives, restart tests, and
  packet validation. Missing a shipped RID lane is DEFER, never PASS.

## Definition of done

- [ ] Every shipped native artifact and RID has reproducible evidence.
- [ ] Embedded lifecycle/read-only behavior satisfies the shared source contract.
- [ ] Native similarity/filter execution and hot-path baselines match the advertised manifest.

## Stop conditions

Unpinned native code, pre-gate file mutation, silent brute-force fallback, absent RID evidence, or production edits
block certification.
