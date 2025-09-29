# Readiness Provisioning Strategy Refactor Plan

**Status**: Draft  
**Author**: AI Assistant  
**Date**: 2024-10-20
**Version**: 0.1

## Executive Summary

Koan's readiness pipeline currently hardcodes schema repair logic inside `AdapterReadinessExtensions.ExecuteWithSchemaProvisioningAsync`, relying on reflection to dispatch a `data.ensureCreated` instruction whenever a repository operation fails due to missing tables or collections.【F:src/Koan.Core.Adapters/Readiness/AdapterReadinessExtensions.cs†L103-L143】【F:docs/guides/deep-dive/auto-provisioning-system.md†L40-L107】 Data adapters such as the Mongo and Couchbase repositories opt-in by wrapping every command in `WithReadinessAsync`, which couples readiness gating with schema-specific heuristics.【F:src/Koan.Data.Mongo/MongoRepository.cs†L95-L103】【F:src/Koan.Data.Couchbase/CouchbaseRepository.cs†L107-L158】 At the same time, AI adapters like Ollama already route chat and embedding requests through the same helper but cannot reuse the provisioning path to pull models because the capability surface advertises only chat/stream/embed flags and lacks any model-management contract.【F:src/Koan.Ai.Provider.Ollama/OllamaAdapter.cs†L98-L220】【F:src/Koan.AI.Contracts/Models/AiCapabilities.cs†L3-L11】【F:src/Koan.AI.Contracts/Adapters/IAiAdapter.cs†L9-L27】 Ollama's discovery flow even performs ad-hoc model downloads during health validation, illustrating the need for first-class AI provisioning hooks.【F:src/Koan.Ai.Provider.Ollama/Discovery/OllamaDiscoveryAdapter.cs†L26-L161】 This plan proposes a break-and-rebuild refactor that generalizes readiness auto-provisioning into pluggable strategies and extends AI capabilities with model provisioning metadata and commands.

## Goals

1. Replace the schema-specific provisioning helper with a strategy pipeline capable of orchestrating schema, queue, and AI model provisioning tasks.
2. Expose a formal model-management capability for AI adapters and route readiness retries through dedicated provisioning strategies instead of bespoke discovery hacks.
3. Tie provisioning outcomes into Koan's broader orchestration story so bootstrapping, readiness, and provenance share consistent instrumentation.

## Current State Analysis

### Readiness Helper
- `AdapterReadinessExtensions` in `Koan.Core.Adapters` wraps all readiness-gated operations and delegates auto-provisioning to `ExecuteWithSchemaProvisioningAsync`, which assumes the failure is schema-related and uses reflection to invoke `IInstructionExecutor<TEntity>` with `DataInstructions.EnsureCreated`.【F:src/Koan.Core.Adapters/Readiness/AdapterReadinessExtensions.cs†L7-L144】
- Schema detection relies on string heuristics and exception type name matching, limiting extensibility and making error diagnosis difficult when unrelated faults trigger retries.【F:src/Koan.Core.Adapters/Readiness/AdapterReadinessExtensions.cs†L150-L164】
- Documentation reinforces the schema-centric framing, emphasizing table/index provisioning without acknowledging other dependency classes.【F:docs/guides/deep-dive/auto-provisioning-system.md†L40-L130】

### Adapter Usage Patterns
- Mongo and Couchbase repositories immediately wrap every command with `WithReadinessAsync`, inheriting the schema auto-provisioning behavior implicitly.【F:src/Koan.Data.Mongo/MongoRepository.cs†L95-L116】【F:src/Koan.Data.Couchbase/CouchbaseRepository.cs†L107-L158】
- Ollama's adapter also uses readiness gating for chat and embeddings but implements custom streaming readiness checks, showcasing mixed usage and limited retry customization.【F:src/Koan.Ai.Provider.Ollama/OllamaAdapter.cs†L92-L191】

### AI Model Management Gap
- `AiCapabilities` only reports service type and high-level feature booleans; `IAiAdapter` exposes no model-management methods.【F:src/Koan.AI.Contracts/Models/AiCapabilities.cs†L3-L11】【F:src/Koan.AI.Contracts/Adapters/IAiAdapter.cs†L9-L27】
- Ollama's discovery adapter attempts to download required models during health validation via direct HTTP calls, with logging but no provenance or capability metadata.【F:src/Koan.Ai.Provider.Ollama/Discovery/OllamaDiscoveryAdapter.cs†L26-L200】
- Because readiness provisioning is schema-only, AI adapters cannot plug their remediation logic into the existing retry pipeline.

## Target Architecture

### Provisioning Strategy Abstractions
1. **Strategy Contract**: Introduce `IAdapterProvisioningStrategy` inside `Koan.Core.Adapters.Readiness` with methods such as `bool CanHandle(Exception ex, ProvisioningContext context)` and `Task<bool> TryProvisionAsync(ProvisioningContext context, CancellationToken ct)`. Strategies receive the adapter instance, optional entity/model metadata, and a failure snapshot to decide whether they can remediate.
2. **ProvisioningContext**: Define a shared context record containing the adapter, the failed operation delegate, the entity or workload type, and any request-specific hints (e.g., AI model identifier, queue name). Reuse it when retrying operations to avoid duplicating closures.
3. **Strategy Pipeline**: Replace `ExecuteWithSchemaProvisioningAsync` with `ExecuteWithProvisioningAsync` that iterates over registered strategies. On failure, it calls the first strategy whose `CanHandle` returns true, executes provisioning, and retries the operation once. If provisioning fails, the original exception bubbles up wrapped with context for observability.
4. **Default Strategies**: Extract existing schema logic into a `SchemaProvisioningStrategy` that uses `IInstructionExecutor<TEntity>` plus `DataInstructions.EnsureCreated`. Provide built-in strategies for message queues or caches in future phases via a shared registration API.

### Adapter Opt-In Model
1. **Capability-Driven Registration**: Expose an `IAdapterProvisioningContributor` interface adapters can implement to supply custom strategies at runtime. During readiness extension execution, collect contributor strategies and append them to the global list so adapters influence the pipeline without modifying core code.
2. **Entity Metadata**: Update repository wrappers (`MongoRepository`, `CouchbaseRepository`, etc.) to pass structured provisioning hints (entity type, logical store name) when invoking readiness helpers. This allows non-schema strategies (e.g., queue topology) to detect the correct target.
3. **Observability Hooks**: Extend `AdapterReadinessMonitor` (or introduce a new event) to emit provisioning start/success/failure signals, enabling provenance recording and telemetry dashboards.

### AI Model Provisioning
1. **Contract Additions**: Expand `AiCapabilities` with a nullable `ModelManagement` object capturing install/flush/refresh support and default registries or provenance scopes. Create an `IAiModelManager` optional interface with methods like `EnsureModelAsync`, `FlushModelAsync`, and `ListManagedModelsAsync` plus provenance payloads.
2. **Provisioning Strategy**: Implement an `AiModelProvisioningStrategy` that inspects exceptions or HTTP responses for model-missing signals, consults the adapter for target model identifiers, and calls `IAiModelManager.EnsureModelAsync`. Include rate-limiting/backoff to avoid thrashing when installs repeatedly fail.
3. **Ollama Implementation**: Factor the existing pull logic out of `OllamaDiscoveryAdapter` into a reusable manager. Populate capability metadata in `OllamaAdapter.GetCapabilitiesAsync` and register the AI model strategy through `IAdapterProvisioningContributor` so readiness retries can trigger model downloads instead of relying on discovery-time side effects.
4. **Provenance Integration**: Emit provisioning events when models are installed or flushed, associating them with orchestration decisions (e.g., container provisioning) to close the loop with Koan's provisioning model.

### Alignment with Koan Provisioning Model
- Surface provisioning metadata through `DependencyDescriptor` extensions, allowing orchestration evaluators to predeclare model prerequisites. Provisioning strategies can consume these hints via the readiness context, bridging container startup with runtime self-healing.
- Record strategy outcomes in the existing readiness state manager so Koan dashboards and bootstrap reports display when schema, queue, or model provisioning occurred.

## Implementation Phases

### Phase 1 – Core Strategy Framework
- Create `ProvisioningContext`, `IAdapterProvisioningStrategy`, and `IAdapterProvisioningContributor` in `Koan.Core.Adapters.Readiness`.
- Refactor `AdapterReadinessExtensions` to call `ExecuteWithProvisioningAsync`, migrating existing schema logic into `SchemaProvisioningStrategy` without altering repository call sites.
- Update documentation to describe the generalized provisioning system.

### Phase 2 – Adapter Integration and Telemetry
- Adjust Mongo, Couchbase, and other repositories to pass context hints (entity type, logical name) when invoking readiness helpers.
- Extend readiness monitors to capture provisioning events and emit structured logs/metrics.
- Provide fallback strategies for adapters that do not implement contributors to preserve current behavior.

### Phase 3 – AI Model Provisioning
- Update `AiCapabilities` and `IAiAdapter` contracts, introducing `IAiModelManager` and request/response DTOs.
- Implement `AiModelProvisioningStrategy` leveraging the new contracts; wire it into readiness pipelines when adapters expose model management.
- Refactor Ollama provider to implement `IAiModelManager`, register its strategy, and move discovery-time downloads into the provisioning pipeline with provenance logging.

### Phase 4 – Koan Provisioning Alignment
- Propagate provisioning hints from orchestration evaluators into readiness contexts (e.g., required models defined in `DependencyDescriptor`).
- Update orchestration boot reports to display strategy results and provide manual override tooling if automated provisioning fails.

## Migration Considerations
- Existing adapters continue to function because `SchemaProvisioningStrategy` preserves the old behavior; tests should verify no regression in schema auto-creation.
- AI adapters need to implement the new capability contract; provide default stubs returning `ModelManagement = null` to avoid breaking implementers.
- Document migration steps for third-party adapters, including how to register custom strategies.

## Risks and Mitigations
- **Risk**: Exception classification could become ambiguous with multiple strategies. *Mitigation*: Evaluate strategies in priority order and require explicit `CanHandle` logic with diagnostics when no strategy matches.
- **Risk**: Model installs may be long-running and block request threads. *Mitigation*: Offload heavy provisioning to background operations while readiness waits, enforcing per-strategy timeouts.
- **Risk**: Breaking downstream consumers expecting the old capability schema. *Mitigation*: Version `AiCapabilities` contract with additive fields and maintain backward compatibility at the API layer.

## Testing Strategy
- Unit tests for `ExecuteWithProvisioningAsync` covering successful provisioning, failure fallback, and multi-strategy priority.
- Integration tests for Mongo/Couchbase verifying schema provisioning still occurs.
- Acceptance tests for Ollama verifying readiness-triggered model pulls and capability exposure through `/ai/capabilities`.
- Telemetry validation ensuring provisioning events surface in readiness monitors and orchestration logs.

## Documentation Updates
- Refresh `docs/guides/deep-dive/auto-provisioning-system.md` to describe strategy-based provisioning, new AI model flows, and orchestration alignment.
- Add API documentation for `ModelManagement` capabilities and `IAiModelManager` usage examples.
- Provide a migration guide for adapter authors adopting provisioning contributors.

