# Data Adapter Acceptance Criteria

This document defines testable acceptance criteria that all Sora Data adapters must meet. It complements the authoring checklist and testing guide with normative MUST/SHOULD/MAY language, and ties behavior to provider capabilities.

Scope: Adapters implementing repositories for entities (`IEntity<TKey>`) across Relational, Document, and similar stores.

Audience: Adapter authors and maintainers; reviewers; test writers.

## Terms

- MUST: required for acceptance.
- SHOULD: strongly recommended; acceptable to defer with rationale.
- MAY: optional.

## 1) Contract surface and discovery

- MUST implement `IDataRepository<TEntity, TKey>` for supported aggregates.
- MUST advertise capabilities:
  - Write: implement `IWriteCapabilities` and expose `WriteCapabilities` flags (`BulkUpsert`, `BulkDelete`, `AtomicBatch` where supported). If native bulk paths exist, also implement `IBulkUpsert<TKey>`/`IBulkDelete<TKey>`.
  - Query: implement `IQueryCapabilities` and set `QueryCapabilities` flags (`Linq`, `String`) as applicable; implement `ILinqQueryRepository<TEntity, TKey>` and/or `IStringQueryRepository<TEntity, TKey>` accordingly.
- MUST register provider discovery via the adapter’s initializer and declare priority with `ProviderPriorityAttribute` where needed.
- MUST bind options from `IConfiguration` using the established helper patterns; provide sensible defaults (see decision 0015). Do not crash if `IConfiguration` is absent in non-host apps.

## 2) CRUD baseline

- MUST implement Create/Read/Upsert/Delete semantics consistent with `IDataRepository`.
- MUST preserve `Identifier` as the storage primary key/unique key. If a user redundantly indexes the identifier, adapters SHOULD deduplicate the index declaration.
- MUST round-trip complex properties (object/collection) according to store conventions (e.g., relational adapters map to JSON columns; see decision 0004).

## 3) Bulk operations

If the backing database exposes native bulk operations:
- MUST implement native bulk upsert and delete.
- MUST set `WriteCapabilities.BulkUpsert` and/or `WriteCapabilities.BulkDelete` true and implement the corresponding marker interfaces.
- SHOULD stream or chunk for large sets where the database benefits from it.

If the backing database does not expose native bulk operations:
- MUST still honor `UpsertManyAsync/DeleteManyAsync` semantics by falling back to efficient single-row operations, batching network round-trips when feasible.
- MUST leave `WriteCapabilities.Bulk*` flags false and NOT implement the bulk marker interfaces.

## 4) Batches and transactions

- MUST implement `IBatchSet` execution over repository operations.
- MUST honor `BatchOptions.RequireAtomic`:
  - If the database supports transactions: execute the batch in a transaction; on failure, no operation is committed; return a batch-level failure with counts and the first error.
  - If transactions are not supported: when `RequireAtomic = true`, return a NotSupported result/exception (per contract); when false, perform best-effort execution and report per-item failures and counts.
- MUST propagate `CancellationToken` through the entire batch path; cancellation SHOULD abort outstanding IO promptly and report `TaskCanceledException`.

## 5) Concurrency control

- If the store supports optimistic concurrency tokens (rowversion/etag):
  - MUST persist and compare the concurrency token on writes; conflicting updates MUST fail with a concurrency error per contract.
  - MUST increment/update the token on successful writes and round-trip it on reads.
- If not supported: MAY emulate with last-write-wins, but MUST NOT advertise concurrency support.

## 6) Query capabilities and pushdown

- LINQ support:
  - If `QueryCapabilities.Linq` is set: MUST translate supported predicates to native queries with server-side filtering; on unsupported shapes, either throw NotSupported (strict) or perform mixed pushdown with safe in-memory completion as documented.
  - Relational adapters that do not ship a full LINQ provider SHOULD use the framework's lightweight LINQ-to-expression translator in the relational toolkit (see [decision 0007](../decisions/DATA-0007-relational-linq-to-sql-helper.md)) to push down common predicates via the provider dialect (e.g., `ILinqSqlDialect`).
- String queries:
  - If `QueryCapabilities.String` is set: MUST accept provider-appropriate query strings and parameterization; MUST avoid string interpolation vulnerabilities.
- JSON filter language and controller usage: adapters SHOULD push down filters and ordering where possible; paging pushdown is REQUIRED when the database supports it. When pushdown is not possible, the framework’s safe in-memory fallback applies (see decisions 0029 and 0032), but adapters MUST minimize materialized result sets (stream/chunk and enforce upper bounds).
- Paging:
  - MUST push down `Skip/Take` (or equivalent LIMIT/OFFSET) natively when the backing database supports it (e.g., SQL LIMIT/OFFSET/FETCH, Mongo `limit`/`skip`).
  - If the database lacks native paging, MUST maintain correctness via in-memory paging, keep memory bounded (stream or chunk), and return accurate totals using `CountAsync` when available.
  - MUST avoid materializing unbounded result sets; adapters SHOULD apply sensible default limits when no explicit paging is supplied and document the policy.
  - MUST expose and honor `DefaultPageSize` and `MaxPageSize` options; enforce `MaxPageSize` server-side when pushdown is available, and enforce in fallback paths otherwise.

## 7) Storage naming and schema

- MUST resolve physical names via `StorageNameRegistry` and the provider’s `INamingDefaultsProvider`; do not implement parallel naming logic inside repositories (see decisions 0017, 0018, 0030).
- Relational adapters SHOULD use the shared relational schema toolkit to generate/apply schema and indexes.
- MUST honor DataAnnotations where feasible: Required, DefaultValue, MaxLength, and Index. Where the store cannot enforce a constraint, MUST preserve data fidelity and document the limitation.
- MUST explicitly announce whether the adapter can create or migrate schema, and expose an idempotent ensure-created path when supported. If schema creation/migration is not supported or not permitted in the environment, MUST return a clear NotSupported response.
- SHOULD favor a least-impedance schema for root aggregates: map primitive root properties to native columns; persist complex roots as JSON (per decision 0004) unless the provider has a first-class feature that better preserves fidelity. Index the identifier and commonly filtered primitive columns. Document any deviations.
- MUST NOT perform destructive changes (drops or incompatible alters) during ensure-created by default. Destructive migrations MUST require an explicit, documented opt-in (e.g., an `AllowDestructive` option) and SHOULD be confined to dedicated migration instructions.

## 8) Instruction execution

- If the provider supports executing SQL/commands: SHOULD implement `IInstructionExecutor<TEntity>` to enable ensure-created/migrations/non-query/scalar/reader operations with parameter binding.
- Adapters that support schema actions MUST implement an idempotent ensure-created instruction (e.g., `data.ensureCreated` or `relational.schema.ensureCreated`). Migrations MAY be supported but MUST be explicit opt-in and documented.
- MUST parameterize inputs (e.g., via Dapper) to avoid injection vulnerabilities.
- If an instruction is unsupported or disallowed by configuration/policy, MUST return a clear NotSupported result/exception with actionable guidance.

## 9) Diagnostics, health, and observability

- Logging: SHOULD log at Debug/Information for key lifecycle events and at Warning/Error for failures using consistent categories.
- Tracing/Metrics: MUST participate in OpenTelemetry if present (activity scopes around query/batch/command paths), without introducing a hard dependency. Use `ActivitySource` and standard db.* attributes; avoid sensitive data in spans.
- Health: MUST provide an `IHealthContributor` that performs a lightweight readiness probe (e.g., directory probe for JSON; trivial command/PRAGMA for SQLite) and register it by default in the adapter’s initializer.

## 10) Options, configuration, and defaults

- MUST bind options from configuration sections named consistently (`Sora:Data:<Adapter>` or provider-specific sections) using provided helpers.
- MUST provide sane dev defaults (e.g., SQLite file path; Mongo default host per decision 0043), and respect environment overrides.
- MUST fail fast with actionable messages when critical configuration is missing.
 - MUST provide paging guardrails:
   - `DefaultPageSize` (applied when the caller does not specify paging) and `MaxPageSize` (an upper bound), with clear documentation of their defaults.
   - A production safety policy to disable or strictly limit in-memory filtering/paging (fallbacks). Respect the platform-level `Sora:AllowMagicInProduction` if applicable; adapters MAY also expose a provider-specific override.

## 11) Error semantics and safety

- MUST translate provider-specific errors to meaningful exceptions/results that align with Sora contracts (e.g., not-found vs concurrency vs validation).
- MUST avoid partial writes in atomic batch mode; in best-effort mode, MUST report per-item outcomes and accurate counts.
- MUST ensure all external inputs are validated/parameterized before execution.

## 12) Acceptance tests (minimum)

Adapters MUST include tests that verify the following:
- Capabilities: `IWriteCapabilities`, `IQueryCapabilities`, bulk markers, and (if present) concurrency support.
- CRUD: round-trip with complex property hydration; identifier uniqueness; delete by id.
- Bulk: upsert/delete many happy path; large set behavior; flags reflect true native vs fallback coverage.
- Batch: RequireAtomic=true transaction success and failure rollback; RequireAtomic=false best-effort with partial failures and counts.
- Query: LINQ and/or string queries for common predicates; paging pushdown; `CountAsync` accuracy for totals.
  - Paging guardrails: enforcing `DefaultPageSize` when omitted; capping to `MaxPageSize`; no unbounded materialization; fallback paths remain bounded.
- Naming: provider registers `INamingDefaultsProvider`; repositories consume `StorageNameRegistry`; set-based routing/suffixing.
- Instruction: ensureCreated is idempotent; scalar and nonquery execute with parameter binding; unsupported instructions return NotSupported with clear diagnostics.
- Cancellation: mid-batch and mid-query cancellation raises `TaskCanceledException` and aborts work.
- Health: health contributor passes under normal conditions and fails with clear diagnostics when broken.

## 13) Delivery checklist (PR gate)

Use this checklist in PRs for new or updated adapters. All MUST items are required.

- [ ] Contracts implemented (`IDataRepository`, capabilities, optional markers)
- [ ] Options bound with defaults; discovery/priority registered
- [ ] CRUD + complex type mapping round-trip
- [ ] Bulk upsert/delete (native if supported; fallback otherwise) and flags correct
- [ ] Batch semantics with RequireAtomic honored; cancellation flows
- [ ] Concurrency tokens implemented (if store supports)
- [ ] Query pushdown for supported shapes; paging pushdown required when supported; fallback bounded and documented
- [ ] Paging guardrails: DefaultPageSize/MaxPageSize options wired; production fallback policy present and documented
- [ ] Naming via `StorageNameRegistry`/`INamingDefaultsProvider`
- [ ] Instruction executor (if applicable) with parameterization; ensureCreated is idempotent; unsupported instructions return NotSupported
- [ ] Destructive schema changes are opt-in only and documented; default ensure-created is non-destructive
- [ ] Health contributor registered
- [ ] Tests cover capabilities, CRUD, bulk, batch, query, naming, instruction, cancellation, health
- [ ] Docs updated (adapter README/notes if applicable)

— End —
