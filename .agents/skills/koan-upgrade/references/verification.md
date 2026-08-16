# Upgrade verification

## Dependency coherence

- All participating Koan dependencies resolve on the chosen current train.
- No unintended old or mixed dependency remains.
- Target framework and SDK requirements are met.
- Report exact resolved versions from existing or newly produced build evidence.

## Preserved behavior

Compare affected before and after behavior:

- Entity persistence, IDs, queries, counts, paging, and streaming;
- HTTP routes, verbs, payloads, status, errors, and authorization;
- tenant isolation and sensitive-data policy;
- jobs, events, storage keys, media behavior, AI/vector results, and MCP surfaces;
- provider routing, database/storage names, configuration precedence, and network exposure.

An unrequested contract change is a failure, not cleanup.

## Proof

1. **Behavior:** representative user or operator journeys pass.
2. **Composition:** facts, health, lock, or equivalent evidence shows the intended current modules, routes, and providers.
3. **Correction:** invalid configuration, a missing required dependency, or a denied action fails at the owning boundary with useful guidance.

Run focused checks during migration and the repository's proportional gate at the end. A successful build alone is not enough.

## Data and topology guard

Confirm the upgrade did not select a fallback, create a new empty data store under another name, move or delete records/blobs/vectors/job state, change a provider route, or alter ports, transport, identity issuer, or secret source.

If any such change is required, stop and obtain separate authorization for an application-evolution task through `koan`.

Use "fully upgraded" only when every required seam is proved. Otherwise name the exact completed boundary, blockers, and checks not run.
