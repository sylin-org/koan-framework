---
id: STOR-0007
slug: STOR-0007-storage-dx-helpers
domain: storage
status: accepted
date: 2025-08-24
title: Storage DX helpers (naming and semantics)
---

Decision

- All storage helper methods are asynchronous but do not use the Async suffix to keep names terse.
- Create* helpers write directly via the orchestrator path; Onboard* helpers carry ingest intent and use the same path (pipeline-aware when steps are enabled).
- Helpers default profile/container to empty so DefaultProfile/fallbacks apply.
- Provide utilities for text, JSON, bytes, streams, files, and URLs; include read conveniences and lifecycle helpers.

Rationale

- Consistent with Koan’s terse method naming and DX focus while preserving async behavior.
- Encourages default routing usage without boilerplate.

Follow-ups

- Consider adding transfer/presign helpers when core capabilities land.
- Provide health/metrics around helper usage if useful.

References

- STOR-0001, STOR-0006
