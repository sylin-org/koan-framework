---
type: REFERENCE
domain: data
title: "MongoDB Replacement Lineage"
audience: [architects, maintainers, developers, ai-agents]
status: accepted
last_updated: 2026-07-29
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-29
  status: behavior-pass-strict-defer
  scope: clean-room replacement lineage, source hashes, retirement, and live MongoDB behavior
---

# MongoDB replacement lineage

The replacement starts from commit `86c18819cf03160c20a001d91f3bd2f257fd1a0d` and lands atomically in
`5cf55ab3ab04847d61d6ee1e089c084a76df8f61`. The implementation root was emptied before authoring the replacement;
the former store, client provider, driver-convention graph, filter translator, connection parser, telemetry wrapper,
and serializer helpers listed in `restricted/retirement.json` are absent.

The resulting connector has one `MongoRepository<TEntity,TKey>` execution path for managed and explicit maps, one
compiled `MongoEntityPlan<TEntity,TKey>`, one native query compiler, bounded host client and collection readiness
owners, and one provider-neutral Source integration. No compatibility repository or shadow execution path remains.

Recovery verification on 2026-07-29 restored every source file byte-identically to the pushed checkpoint and ran the
existing exact-source test binary against a fresh MongoDB 8.3.4 container. All 34 cases passed with exit code zero and
zero provider skips. A subsequent lifecycle correction kept the same nine-part architecture while making options pure
and moving discovery into bounded asynchronous first use. An explicitly offline restore from the installed package
cache produced a fresh zero-warning build, and the resulting 35-case suite passed against MongoDB 8.3.4.

Strict packet, topology matrix, stable performance, and independent certification remain deferred. The sealed
greenfield documents prove only atomic replacement lineage, current source identity, justified moving parts, and
retired-path absence; they do not synthesize certification.
