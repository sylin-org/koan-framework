---
type: EVIDENCE
domain: data
title: "DAC-04 verification — source policy, readiness, and failures"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: framework source-plan, policy, readiness, failure, diagnostics, and alternate-path foundation
---

# DAC-04 verification — source policy, readiness, and failures

## Result

PASS for the Framework-owned foundation. A named source now compiles into one immutable, redacted
`DataSourcePlan`; its typed effect gate is the first semantic action on Entity, batch, instruction, and Direct paths.
The shared message-text catch/provision/replay helper is deleted. Reachability, shape validation, and authorized
provision-plus-post-validation use distinct, host-owned, bounded single-flight state with caller cancellation detach
and host-shutdown cancellation.

`Managed + ReadWrite` remains the zero-configuration default. `ReadOnly` blocks data mutation.
`External` independently blocks storage/admin work, permits mapped writes when access is `ReadWrite`, lowers
`Optimized` removal to `Safe`, and keeps structural `Fast` unavailable. Exact instruction identities carry effects;
unknown effects reject on every constrained source. Literal Direct connections retain the source ceiling and are not
inserted into the host plan cache.

## Executable evidence

| Evidence | Result |
|---|---|
| `SourcePolicySpec` and `SourceReadinessCoordinatorSpec` | 49 passed; four policy cells, read lanes, redaction, diagnostics, Direct override/transaction, Entity/batch/instruction order, semantic delete, bounded caches, single-flight, cancellation, recovery, post-validation, and host isolation |
| Source/routing/participation/Direct focused matrix | 89 passed in 7 seconds |
| `Koan.sln` restore-free build | PASS; 0 warnings, 0 errors in 27 seconds |
| Shared replay/message classifier search | PASS; no `WithDataReadiness`, `IsSchemaRelatedFailure`, or schema-message classifier remains under `Koan.Data.Core` |
| Mutation-sensitive order proof | `Read_only_write_rejects_before_guard_readiness_lifecycle_or_provider` becomes RED if policy demand moves after the guard/readiness boundary; the Direct spy cases become RED if resolution/resource creation moves before policy |

The broad Data Core suite was also attempted. It exceeded the 180-second bound with unrelated existing failures in
runtime-fact multiplicity, Windows EventLog permissions, and AI embedding fixtures; it is not used as DAC-04 evidence.
The changed projects, the 89-test owned surface, and the complete solution compile cleanly.

## Ownership proof

- `DataSourceRegistry` owns source discovery, typed lifecycle/access/read-lane compilation, immutable plan memoization,
  credential identities, and invalidation after programmatic registration.
- `RepositoryFacade` owns the first-operation policy gate and never invokes legacy provisioning readiness for a
  constrained source.
- `DataSourceReadinessCoordinator` owns three distinct stage states. Provision succeeds only after the separate shape
  validator succeeds; neither failure nor caller cancellation is cached healthy.
- `InstructionEffect` maps only stable framework instruction identities. SQL/message prefixes and result shapes have
  no authority.
- `IDataFailureClassifier` is the one adapter translation seam. Data owns failure kind, commit outcome, retry
  disposition, replay disposition, and the invariant that committed/outcome-unknown operations are never replayed.
- `IDataDiagnostics` receives a redacted projection of the exact execution plan; adapter settings and raw connections
  are absent.
- `DocumentStore` uses the ordinary non-replaying readiness wrapper. The former catch/provision/replay source file is
  deleted.

## Primer-row disposition

| Rows | DAC-04 disposition |
|---|---|
| A-02, A-03, A-04, A-06 | PASS: exact routing/participation retained; source plan and stage state are host-owned; defaults are executable facts. |
| C-01, C-02, C-03, C-05, C-06 | PASS: first-boundary gates, independent ceilings, external semantic delete, exact effects, and redaction are executable facts. |
| C-04 | PASS for Entity, nested source plan, Direct/connection override, Direct transaction, batch, and instruction paths. Provider extensions and transfer/backup consumers are child proofs under DAC-05/DAC-08. |
| G-01, G-02 | PASS: concurrent readiness/provisioning, distinct state, cancellation detach, shutdown ownership, post-validation, failure recovery, and cache bounds are executable facts. |
| H-01 | PASS for the redacted source-plan diagnostic projection. Complete `Describe` capability/mapping projection remains DAC-08. |
| P-01, P-03 | PASS for source-policy compilation and shared readiness caches. Provider mapping/native-operation/client caches remain adapter certification proofs. |
| A-05, A-07, A-08, A-09 | Child proof: each adapter must release native resources, compare real shape definitions, prove non-creating external open, and run idempotent provision/post-validation through this coordinator. Gold owners: DAC-11/DAC-21; fleet owners: DAC-40–DAC-58. |
| G-03, G-04, G-05 | Child proof: native pool/session reset, cancellation/timeout/resource disposal, rollback, atomicity, and outcome-unknown classification require real-provider evidence under each adapter card and DAC-05. |
| H-02, H-03 | Child proof: complete Explain/Doctor surfaces are owned by DAC-08 and then exercised by every adapter. |

These are evidence dependencies, not framework TODOs or temporary adapter exceptions. The gold adapters must consume
the shared contracts directly; an adapter-local source-policy gate, replay helper, or readiness cache fails P-06.
