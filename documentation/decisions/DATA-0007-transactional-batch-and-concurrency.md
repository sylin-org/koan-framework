---
id: DATA-0007
slug: DATA-0007-transactional-batch-and-concurrency
domain: DATA
status: Proposed
---

# ADR 0007: Transactional batch semantics and optimistic concurrency tokens

Context
- Batches (IBatchSet) currently aggregate Add/Update/Delete operations, but atomicity vs best-effort behavior needs to be explicit and testable.
- Concurrency control is missing; we want an optional, provider-agnostic way to prevent lost updates.

Decision
1) Batch atomicity via options
- Extend the existing `BatchOptions` with a `RequireAtomic` flag (already present) as the contract knob.
- Semantics:
  - When `RequireAtomic = true` and the provider supports transactions, the batch executes in a transaction; on failure, no operation is committed and the result signals a batch-level failure with the first error and counts.
  - When `RequireAtomic = true` but unsupported, the provider returns a NotSupported result (or throws NotSupportedException) and no operations execute.
  - When `RequireAtomic = false`, providers may apply best-effort execution; the result returns per-item failure info and counts, with partial success allowed.
- JSON adapter: emulate best-effort only; atomic mode not supported.
- Relational adapters (SQLite, Postgres): support atomic mode using a DB transaction.

2) Optimistic concurrency tokens (optional)
- Add a `[ConcurrencyToken]` attribute that can be applied to a single scalar property on the aggregate (e.g., `long Version` or `string ETag`).
- Semantics:
  - Upsert: if the entity has a token property and it is non-default, the update must match the current stored token; if mismatch, return a conflict result without applying changes.
  - On successful upsert, providers advance the token (e.g., increment integer version, compute new ETag).
- JSON adapter: persist and compare the token; increment integer or update string token.
- Relational adapters: persist token column; SQLite uses INTEGER version.

Consequences
- Contracts gain clear batch semantics; tests can assert atomic vs best-effort.
- Concurrency support is opt-in and keeps POCO purity via an attribute.
- Some adapters will initially not support atomic batches; callers can detect via capabilities.

Notes
- Update: `Capabilities` may expose `AtomicBatch` and `Concurrency` flags for diagnostics.
- Controllers remain oblivious; the data layer makes the best choice or surfaces NotSupported/conflict results.
