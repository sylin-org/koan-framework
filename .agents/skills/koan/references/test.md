# Proof design

Use this reference when proof is the main task or a capability needs stronger evidence than the change itself supplies.

## The three proofs

| Proof | Question | Typical evidence |
|---|---|---|
| Behavior | Can the user or operator complete the intended story? | Public HTTP/MCP call, Entity operation, job outcome, AI result, stored media |
| Composition | Did the intended capability and provider participate? | Facts, lock evidence, health detail, provider-specific assertion |
| Correction | Does invalid intent fail at the owning boundary with a useful next move? | Missing reference, bad connection, denied identity, unsupported operation, unavailable model |

Compilation is necessary for code changes but does not replace these proofs.

## Choose the narrowest credible test

Start at the public expression named in the intent and cross only the real layers needed for the guarantee. Use deterministic local infrastructure when it proves the same contract, and provider integration when provider behavior is the claim.

Capability minimums:

- **Data/provider:** write/read/query, selected adapter, unsupported operation or bad connection.
- **Data movement:** source inventory, resumable copy/verification, route switch, and recovery boundary.
- **Auth/tenancy:** anonymous denial, allowed action, forbidden action, and cross-tenant denial on every doorway.
- **Jobs:** durable receipt, retry/idempotency, progress/failure, cancellation, and restart where promised.
- **Communication:** acceptance versus settlement, duplicate/retry semantics, and transport outage.
- **Cache:** hit/miss, invalidation, isolation, stale/degraded policy, and authoritative source behavior.
- **Storage/media:** ownership, retrieval, bounds, unauthorized access, and processing failure.
- **AI:** operation/prompt contract, provider route, unavailable provider, cancellation, and sensitive-data policy.
- **Vector:** known-neighbor relevance, dimensions/version, filters/paging, empty/degraded behavior, and selected store.
- **MCP:** discovery, allowed read/action, denied action, resource semantics, caller scope, and transport trust.
- **Canon:** match, no-match, ambiguity, replay, provenance, and commit failure.
- **Operations:** readiness reflects required dependencies; facts explain selection without leaking secrets.

A useful test fails when the requested guarantee breaks. Reject proofs that only assert status codes, construct services without using them, or allow any provider to satisfy a named-provider claim.
