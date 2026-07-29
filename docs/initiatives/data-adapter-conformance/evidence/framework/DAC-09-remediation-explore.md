---
type: EVIDENCE
domain: data
title: "DAC-09 remediation exploration"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: pre-implementation exploration for DAC-09R-01 through DAC-09R-07
---

# DAC-09 remediation exploration

**Task:** Convert independently reproduced DAC-09 RED findings into seven serial, one-owner remediations and return the
same Framework candidate to a fresh independent gate before gold harvests.

**Application intent:** An application expresses a source, operation, mapping, or diagnostic decision once and receives
the same faithful, bounded, explainable behavior through every Data entry point.

**Public expression:** Existing compact expressions remain authoritative: `services.AddKoan(koan =>
koan.Data.Source("LegacyErp")...)`, ordinary `Entity<T>` operations, and
`Data.Source("LegacyErp").Describe()/Explain()/Doctor(ct)`. Adapter authors use one inert `DataClaimSet` and lower frozen
Framework/Family plans. The complete surface is the referenced connector, source configuration/declarations, mapping or
registered operation declarations when needed, ambient Entity context, and provider runtime for LIVE work.

**Guarantee/correction:** Composition freezes decisions; one explicit effect gates before dispatch; fallback is selected
before dispatch; work and state are bounded and host-owned; diagnostics are inert/redacted; claims have one identity.
Unsupported, ambiguous, late, or over-bound intent fails before provider work with a stable correction.

**Complete intent surface:** No new application action is introduced beyond the existing expression. Direct callers must
state the effect of opaque work; adapter authors must declare the one claim set and exact native receipts.

**Public concepts:** `DataOperationEffect` exists because opaque work cannot derive safety from result shape;
`DataClaimSet` exists because advertised guarantees need one executable identity; existing typed bounds exist because
resource safety is observable behavior. No additional public concept is justified.

**Docs read:** `docs/architecture/principles.md` makes business intent, one composition, thin hot paths, semantic honesty,
and unified explanation constitutional; `docs/architecture/data-adapter-development-primer.md` supplies the normative
A-H/P rows and forbids effect inference, replay, hidden scans, unbounded caches, and public native evidence;
`docs/architecture/data-adapter-responsibility-map.md` assigns each decision one Framework/Family/Adapter owner;
`ACCEPTANCE.md` requires RED certification to create bounded cards and invalidate consumers; `DAC-09` forbids fixing
inside certification and blocks gold harvests.

**Code read:** `KoanDataSpec.cs` currently offers only parameterless `AddKoan()` before its DI hook;
`ManagedFieldNoLeak.cs` registers after boot; `DirectSession.cs` derives Read from scalar/query result shape and repeats
reflection/DI work; `RepositoryFacade.cs` and `EntityTransferBuilderBase.cs` fall back after ambiguous exceptions and
materialize full sources; `DataSourceRegistry.cs`, `DataService.cs`, and `DataSourceIntegrationService.cs` admit mutable or
unbounded host state; `DataClaimSet.cs` and TestKit manifest selection have parallel capability authorities;
`DataAxis.cs` constructs repositories for Explain; transaction paths expose identifiers/native exceptions;
`RecordProjector.cs` and `PatchOpsExecutor.cs` bypass promised completeness/compiled mapping behavior.

**Reusing:** immutable `DataSourcePlan`, `DataOperationEffect`, registered `OperationPlan`, `DataClaimSet`,
`RecordSetMaterializer`, mapping plans, query receipts, stable `DataFailure`, restricted evidence store, and typed runtime
options already exist and remain the semantic owners.

**Creating new:** Each remediation may add only a private/internal bounded plan/cache/helper or typed option proven
necessary by its card. R01 adds a test-host composition hook and compile inclusion; R02 may add finite source/repository
capacity options; later cards first delete bypasses before adding any part.

**Coalescence:** Closest patterns are registered operations for effect/bounds, `DataMappingPlans` for bounded host-owned
single compilation, and `DataSourceDiagnosticsService` for inert explanation. Their meaning/lifetime match the target,
so bypasses are absorbed or deleted. Connector-local gates, serializers, registries, and caches remain forbidden.

**Ergonomics:** Application code keeps the compact Source/Entity language. IntelliSense exposes an effect only where
opaque native work genuinely requires the decision. No assembly/service-provider machinery reaches application code.

**Constraints satisfied:**

- No HTTP surface or inline endpoint is involved.
- Entity statics remain the normal data path; Direct remains an explicit escape hatch.
- Stable identifiers and default bounds use project constants/typed options.
- Large transfer paths must use capability-qualified paging, never hidden `All()` materialization.
- No placeholder, adapter-local policy copy, or process-global mutable owner is permitted.
- Canonical docs and certification receipts are updated with behavior.

**Risks:** Direct compatibility may require an explicit breaking correction; claim convergence can make legacy adapters
louder before their scheduled rewrites; patch semantics must not be changed until its mapping-plan target is explicit;
checkpoint replay must preserve the dirty-workspace identity without implying a commit.

## R03 operation/effect placement

The focused trace found four ways result syntax currently becomes effect authority: Direct `Scalar`/`Query` demand
`Read`; `InstructionEffect` treats raw SQL scalar/query identities as proven reads; both Core and Relational string-SQL
extensions inspect `StartsWith("select ")`; and `Data<TEntity>.Execute<TResult>(string)` chooses scalar/non-query from
`TResult`. Registered operations also expose a duplicate public `OperationEffect` enum and activate the provider before
rejecting an opaque binding that lacks a lane.

R03 therefore uses the existing `DataOperationEffect` everywhere, removes the duplicate enum and ambiguous string-SQL
extensions, makes raw SQL instructions `Unknown` unless the caller supplies an effect, and adds one compact
`Direct(...).Effect(...)` declaration inherited by a transaction. The registered-read builder remains a semantic Read
surface, but static effect/lane contradictions move ahead of integration activation. Direct/entity reflection forwards
the caller token. No connector or provider parser is allowed to classify command text.
