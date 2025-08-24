---
id: STOR-0004
slug: STOR-0004-storage-ingest-pipeline-and-policy-steps
domain: STOR
title: Storage ingest pipeline phases and policy steps
status: Accepted
date: 2025-08-24
---

Context

- We need configurable hooks around file ingestion to enforce policies (size/MIME), scan content (virus/DLP), enrich metadata, and optionally quarantine.
- Hooks must remain separate from IO providers to keep adapters thin.

Decision

- Define IStoragePipelineStep with two phases and deterministic ordering:
  - OnReceiveAsync(context): before write; validate headers/size/MIME; may mutate tags/metadata.
  - OnCommitAsync(context): after write (staging or final); may scan content, enrich metadata; can quarantine.
- Step outcomes: Continue, Stop(reason/code), Quarantine(reason), Reroute(profile), Mutate(metadata/tags).
- Staging strategy (per-profile): None | LocalTemp | ProviderShadow; required when any step needs full-blob inspection.
- Steps declare Need: Sequential (default) or Full; orchestrator stages only when the Need requires it.

Scope

- In scope: step contracts, outcomes, staging strategies, ordering semantics, error handling.
- Out of scope: a general-purpose pipeline DSL; steps are registered via DI with order.

Implementation notes

- Compute ContentHash while streaming to staging/final; avoid double reads.
- Timebox steps and handle timeouts per profile policy (reject vs quarantine-on-timeout).
- Emit audit events for step start/finish/fail with reason codes.
- Expose ProcessingStatus (Pending|Verified|Quarantined|Rejected) on the entity and control HTTP access accordingly.

Consequences

- Positive: per-profile compliance without bloating providers; predictable short-circuiting and minimal buffering.
- Negative: introduces complexity in orchestration; mitigated by staging only when necessary and clear diagnostics.

References

- STOR-0001 Storage module and contracts
- STOR-0002 Storage HTTP endpoints and semantics
