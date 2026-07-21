# ARCH-0087: Unified service-discovery adapter model — retire V1, complete the RabbitMq adapter

**Status**: Accepted (2026-06-15) — Enterprise Architect sign-off (E2 fork resolution: "author the RabbitMq adapter now").
**Date**: 2026-06-15
**Deciders**: Enterprise Architect
**Scope**: Retire the legacy V1 service-discovery resolver (`OrchestrationAwareServiceDiscovery`) from `Koan.Core`, bringing the last holdout connector (RabbitMq) onto the canonical V2 adapter/coordinator model, and drop the now-meaningless `V2` suffix.
**Related**: ARCH-0049 (unified service metadata + discovery) · ARCH-0068 (discovery base class, `ServiceDiscoveryAdapterBase`) · ARCH-0077 (the broader orchestration→Aspire migration — still *Proposed*; this ADR makes the *bespoke* model internally consistent in the meantime, and the per-connector `[KoanService]` descriptors it touches are exactly what ARCH-0077 later repackages). Resolves the E2 card fork (docs/assessment/prompts/06/E2-delete-v1-service-discovery.md).

---

## Context

Koan has carried **two** service-discovery resolvers:

- **V1 — `OrchestrationAwareServiceDiscovery`** (`Koan.Core.Orchestration`): a self-contained per-call resolver. `DiscoverService(name, options)` runs a fixed 5-phase pipeline (Aspire → explicit config → env vars → orchestration candidates → fallback) and **always returns a candidate**, needing no registered adapter. RabbitMq-specific knowledge lived in `ServiceDiscoveryExtensions.ForRabbitMQ()`.
- **V2 — `OrchestrationAwareServiceDiscoveryV2` + `IServiceDiscoveryCoordinator`** (the canonical model since ARCH-0049/ARCH-0068): a pure coordinator that routes `DiscoverService(name)` to a per-connector `IServiceDiscoveryAdapter` (each subclassing `ServiceDiscoveryAdapterBase`, which centralises the container/local/Aspire candidate logic and reads the connector's own `[KoanService]` attribute for host/port/scheme). Twelve data/AI connectors already ship a discovery adapter.

The E2 assessment cartography established the current truth: **V1 has exactly two remaining callers, both RabbitMq** (`RabbitMqProvider.GetOrchestrationAwareConnectionString` at runtime; the registrar's `Describe()` boot-report). The two Vault call sites the original card expected are gone — C7 migrated Secrets to agyo. **RabbitMq is the sole connector with no `IServiceDiscoveryAdapter`** — it never got one, which is precisely why it still uses V1.

A naive "route RabbitMq through V2" therefore **regresses discovery**: the coordinator finds no `rabbitmq` adapter, returns `NoAdapter`, and the resolution collapses to a hard-coded `localhost` fallback — losing V1's `rabbitmq:5672` resolution in compose/k8s/container modes. Additionally one of the two call sites is the registrar's `Describe()`, which has no `IServiceProvider` for DI-based coordinator resolution.

## Decision

Complete the V2 model for RabbitMq, then retire V1:

1. **Author the missing adapter.** Add `RabbitMqServiceDescriptor` carrying `[KoanService(ServiceKind.Messaging, "rabbitmq", "RabbitMQ", Scheme="amqp", Host="rabbitmq", EndpointPort=5672, LocalScheme="amqp", LocalHost="localhost", LocalPort=5672)]` (a dedicated descriptor type, mirroring the `LMStudioServiceDescriptor` precedent — keeps the provider clean), and `RabbitMqDiscoveryAdapter : ServiceDiscoveryAdapterBase` (`ServiceName="rabbitmq"`, `GetFactoryType() => typeof(RabbitMqServiceDescriptor)`, `GetEnvironmentCandidates()` preserving the legacy `RABBITMQ_URL`/`Koan_RABBITMQ_URL`, and a **real AMQP health check** via `RabbitMQ.Client` — parity with how `RedisDiscoveryAdapter` does a real ping). Register it in the RabbitMq registrar (`TryAddEnumerable<IServiceDiscoveryAdapter, RabbitMqDiscoveryAdapter>`).

2. **Migrate both V1 sites onto the adapter.** The runtime path (`RabbitMqProvider`, DI-constructed) resolves through the injected `IServiceDiscoveryCoordinator` (the canonical V2 entry point). The DI-less `Describe()` boot-report path constructs the adapter directly (`new RabbitMqDiscoveryAdapter(cfg, NullLogger)`), exercising the same resolution logic. With the adapter registered, discovery no longer regresses.

3. **Retire V1.** Delete `OrchestrationAwareServiceDiscovery`, `IOrchestrationAwareConnectionResolver`, `OrchestrationAwareConnectionResolver`, and the V1-only `ServiceDiscoveryExtensions.ForRabbitMQ()` helper, after verifying zero remaining references.

4. **Drop the suffix.** Rename `OrchestrationAwareServiceDiscoveryV2` → `OrchestrationAwareServiceDiscovery` (the `V2` distinction is meaningless once V1 is gone). The class is registered and consumed only through `IOrchestrationAwareServiceDiscovery`; its concrete name never appears in a public method/return/parameter signature, so the rename is internal in practice. Koan is pre-1.0 (NBGV 0.x), so the nominal public-type rename is an acceptable 0.x change.

## Consequences

- **One discovery model.** Every connector — RabbitMq included — now discovers through the `ServiceDiscoveryAdapterBase` + `IServiceDiscoveryCoordinator` path. The legacy self-contained resolver is gone; there is no second way to do discovery.
- **No behavior regression.** RabbitMq still resolves `amqp://rabbitmq:5672` in compose/k8s and localhost in self-orchestration, plus the legacy env vars, now via its adapter rather than V1's bespoke pipeline. Health validation is a real AMQP connect.
- **The descriptor pattern generalises.** A dedicated `[KoanService]` descriptor type (vs an attribute on a factory) is the clean way to give a non-data connector an orchestration identity; future messaging/other connectors follow the same shape.
- **ARCH-0077 alignment.** This consolidates the bespoke discovery model rather than competing with the Aspire migration: when ARCH-0077 lands, the per-connector `[KoanService]` descriptors (now uniform) are the exact surface it repackages as `Koan.Aspire.Hosting.<Service>` integrations.
- **Migration note.** Any out-of-tree 0.x consumer referencing the concrete `OrchestrationAwareServiceDiscoveryV2` type name (none known in-repo) updates to `OrchestrationAwareServiceDiscovery` or, preferably, the `IOrchestrationAwareServiceDiscovery` interface.
