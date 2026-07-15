---
type: SPEC
domain: framework
title: "R07-05 - Canonical Entity Lifecycle"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-15
  status: passed
  scope: host ownership, canonical Data boundary, affected regression, 0.18 lineage, docs, diff, and privacy closure
---

# R07-05 — Canonical Entity Lifecycle

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-04
- Unlocks: typed capability substrate
- Owner: Data persistence semantics and host composition

## Meaningful outcome

Application code names persistence intent honestly and declares it with the host:

```csharp
builder.Services.AddKoan(() =>
    Order.Lifecycle.BeforeUpsert(context =>
        context.Current.Total < 0
            ? context.Cancel("Order total cannot be negative.", "order.total")
            : context.Proceed()));
```

The same policy is unavoidable through Entity, Data, generated REST, and generated MCP paths. The
plan is inspectable, belongs to exactly one host, and introduces no process reset protocol. The old
`Entity.Events` persistence name and its registry/executor/batch machinery are deleted without an
alias in one explicit Data.Core 0.18 breaking wave.

## Architecture

- `AddKoan(Action)` supplies one flow-local composition scope while preserving parameterless
  `AddKoan()` as the complete zero-configuration bootstrap. Framework modules enter the same scope.
- Static `Entity<T>.Lifecycle` is syntax only. It resolves the current `IServiceCollection` and
  declares one host-owned `EntityLifecyclePlan<T,TKey>`.
- One outer `RepositoryFacade` owns guards, isolation, transforms, Lifecycle, and truthful timing.
  Provider/module decorators—including cache—sit inside it and cannot short-circuit those guarantees.
- Before/After Load, Upsert, and Remove are the complete public phase set. `Setup`, mutable operation
  state, faux atomic batch flags, and aggregate outcome types are removed.
- Bulk upsert preflights rejection before the first write. Configured handlers use pointwise
  persistence so after-handlers correspond to actual writes; no-handler code retains native bulk.
- Patch lowers to canonical read/mutate/upsert. Soft-delete batches lower to canonical updates and
  reject `RequireAtomic=true` rather than claim false atomicity.
- `Safe` and `Optimized` preserve configured remove Lifecycle. Explicit `Fast` is a documented bypass.
- Composition facts report entity and phase counts through `koan.data.lifecycle.selected`.

## Deliberate boundaries

- Lifecycle is persistence policy, not domain Events and not Transport.
- Load means materialization at the canonical repository boundary. Handlers should remain small and
  deterministic; substantial read projections belong elsewhere.
- Provider-native instructions/raw queries remain explicit escape hatches rather than modeled Entity
  persistence.
- `UpsertIfChanged` compares before Lifecycle and enters Lifecycle only for a real write.
- Lifecycle does not promise cross-provider or lowered soft-delete batch atomicity.

## Evidence

- Data.Core focused Lifecycle: 9/9 — cancellation, Data/Entity read parity, lazy prior, protection,
  bulk preflight, transaction timing, repeated hosts, corrective composition failure, runtime facts.
- Full affected regression: Data.Core 347/347; Data.AI 84/84; Web Extensions 111/111; MCP
  Conformance 75/75; Identity 114/114; OpenGraph 38/38; SoftDelete 9/9; Backup 7/7; Cache
  CrossEngine 14/14; Entity language 11/11; Core Unit 112/112; Canon unit 35/35 and integration 6/6.
- Cache topology passes 50/50. Focused soft-delete batch passes 2/2, including fail-closed atomic intent.
- Packaging passes 54/54, including automatic breaking lineage. The Data.Core intent is 0.18 and the
  four tracked composition lockfiles record that exact tier; reverse-dependent identities remain
  automation-owned.
- Release solution build succeeds with 0 errors and the reviewed 21-warning baseline. Its one new
  Canon `Lifecycle` collision caused deletion of a redundant convenience property; Canon Domain then
  builds with 0 warnings and both Canon suites pass.
- Docs lint: 0 errors / 1568 historical warnings. Changed examples: 2/2. Skills: 20/20. Blueprint:
  1/1. Surface lint: 34 rows. `git diff --check` and the privacy scan pass.

## Acceptance

- No production or sample persistence reference remains to the old `Entity.Events` surface.
- Sequential hosts using one Entity type have independent plans without reset hooks.
- Entity/Data and generated REST/MCP mutations prove the same canonical boundary.
- After-handlers never run before a failed write or an uncommitted Koan transaction.
- Startup/operator/agent facts expose the composed plan without handler targets or sensitive payloads.
- Public current guidance documents composition, phases, bulk/transaction semantics, and bypasses; old
  ADRs are clearly marked historical/amended.
- The affected full regression, solution build, packaging-lineage contract, docs, compatibility, diff,
  and privacy gates pass once at tranche closure. The public-release certification ratchet is not
  rerun for this ordinary slice.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-15; closure commit recorded immediately after this card update
- Evidence: host-owned composition; one outer Data facade; old implementation deleted; current
  reference, README, package companion, initiatives, migrated modules, generated REST/MCP facts.
- Tests / validation: affected counts and repository gates listed above; no public-release
  certification rerun was used for this ordinary slice.
- Unsupported scenarios: cross-provider batch atomicity; atomic lowered soft-delete batches;
  Lifecycle guarantees for explicit provider-native escape hatches; per-entity handlers on explicit
  `RemoveStrategy.Fast`.
- Follow-up work: typed capability substrate is the next R07 child. Communication Events/Transport
  remain unopened.
- Reviewer: Codex implementation and executable evidence under maintainer's standing approval.
