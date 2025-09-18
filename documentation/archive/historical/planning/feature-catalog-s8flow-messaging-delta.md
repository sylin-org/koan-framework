# S8.Flow Messaging: Remaining Integration Delta (Post-ADR MESS-0070)

## Overview
Tracks the shrinking gap to full S8.Flow operational readiness on the greenfield, primitives‑driven messaging stack (ADR MESS-0070 / MESS-0071). Updated after enabling centralized orchestrated plan→inspect→diff→apply (RabbitMQ), diagnostics timing + plan hash, and FlowEvent version suffix parity.

---

## Summary Table

| Area                        | Status              | Delta/Action Needed                                                  |
|-----------------------------|--------------------|----------------------------------------------------------------------|
| Topology planner invocation | Done               | Central orchestrator hosted service runs once at startup             |
| DI/service registration     | Done               | All planner + orchestrator + RabbitMQ differ/applier wired           |
| Naming/group resolution     | Done (baseline)    | Possible future: custom org prefixes / multi-tenant adorners         |
| Handler discovery           | Optional (Deferred)| Could add handler scan to prune unused primitives (low priority)     |
| DLQ/retry support           | Partial            | Retry bucket generalization + centralized creation still pending      |
| Diagnostics/logging         | Advanced (Rabbit)  | Provider-agnostic diff + structured events pending                    |
| ProvisioningMode handling   | Partial Centralized| Core mode evaluation done; reconcile / destructive semantics pending  |
| Docs/samples                | Partial            | Need orchestrator, diagnostics fields, FlowEvent version guidance     |

---

## Detailed To-Do List

1. **Wire Topology Planner into Startup**
   - [x] Register and invoke `DefaultTopologyPlanner` (hosted orchestrator handles plan/diff/apply).

2. **DI Registration and Service Wiring**
   - [x] Register `DefaultTopologyPlanner` (singleton) + hosted orchestrator service.
   - [x] RabbitMQ provisioner registered across planner/inspector/differ/applier interfaces.

3. **Provider-Specific Naming/Group/Domain/Adapter Resolution**
   - [x] Baseline via `DefaultTopologyNaming` + `MessagingOptions` (bus / group / version flag).
   - [ ] Extensions: organization / environment prefix strategy (optional).

4. **Handler/Consumer Discovery (Optional)**
   - [ ] Add optional handler scan (currently primitive scan sufficient for S8 scope).

5. **DLQ, Retry, and Advanced Topology Features**
   - [x] Emit `DlqSpec` / `RetryBucketsSpec` in planner output when enabled.
   - [x] Introduced `IAdvancedTopologyProvisioner` & consumed by RabbitMQ.
   - [x] FlowEvent version suffix parity implemented.
   - [ ] Generalize retry bucket queue/exchange creation (currently provider local logic).
   - [ ] Central policy: differentiate Additive vs ForceRecreate semantics for DLQ/retry drift.

6. **Diagnostics and Observability**
   - [x] Emit provisional plan snapshot via `IMessagingDiagnostics`.
   - [x] Orchestrated inspection + diff + apply (RabbitMQ path) with timing metrics.
   - [x] Added `ProvisioningDiagnostics` fields: `DesiredPlanHash`, `PlanMs`, `InspectMs`, `DiffMs`, `ApplyMs`.
   - [ ] Provider-agnostic diff abstraction for non-Rabbit providers.
   - [x] Persist last plan hash (file / cache) for no-op fast path.
   - [ ] Structured logging events (OTel) for each phase.

7. **Production Safety and ProvisioningMode Enforcement**
   - [x] Centralized mode evaluation (env override + environment heuristic) in orchestrator.
   - [x] Implement shared guard + audit for `ForceRecreate` (env `Koan_MESSAGING_ALLOW_FORCE=1`).
   - [ ] Normalize provider-level mode handling (RabbitMQ internal fallback still performs own evaluation).
   - [ ] Uniform DryRun diff logging format across providers.

8. **Documentation and Samples**
   - [ ] Expand docs for orchestrator lifecycle + diagnostics field semantics.
   - [ ] Add guidance for interpreting timing metrics & plan hash.
   - [ ] Update S8.Flow sample README to remove any legacy provisioning references.
   - [ ] Add minimal recipe: enabling retry + DLQ safely in production.

9. **New / Emerging Items**
   - [x] Plan hash persistence & startup short-circuit.
   - [ ] ReconcileAdditive vs ForceRecreate concrete rule matrix (doc + code).
   - [ ] Retry bucket generalization abstraction.
   - [ ] Optional handler scan toggle (`MessagingOptions.EnableHandlerDiscovery`).
   - [ ] Structured events → OTel semantic conventions proposal.

---

## References
- [ADR MESS-0070: Messaging Topology, System Primitives, and Zero-Config Developer Experience](./decisions/MESS-0070-messaging-topology-system-primitives-zero-config.md)
- [Koan.Messaging primitives and provisioning source]

---

## Recent Changes (2025-09-03)
* Added centralized `TopologyOrchestratorHostedService` (plan → inspect → diff → apply) with provider client accessor (RabbitMQ).
* `ProvisioningDiagnostics` enriched with plan hash + phase timings (plan/inspect/diff/apply) and version hash logic.
* RabbitMQ factory now reports timing + hash; orchestrator sets plan mode from centralized evaluation.
* FlowEvent exchange naming now respects `IncludeVersionInAlias` flag.
* Advanced provisioner path (queue args including DLQ / retry specs) exercised via orchestrated diff.

**Status:** In progress, core lifecycle functional for RabbitMQ; remaining work focuses on generalization, persistence, and documentation polish.
