---
id: AI-0019
slug: AI-0019-koan-ai-meai-zero-config
domain: AI
status: Accepted
date: 2025-11-18
---

# ADR: Koan.AI zero-config integration on Microsoft.Extensions.AI

**Contract**

- **Inputs:** `Koan:Ai:*` configuration values (optional), discovered Microsoft.Extensions.AI clients and middlewares, Koan auto-registrar pipeline, adapter metadata emitted by Koan.Providers.
- **Outputs:** Default conversational and embedding pipelines registered behind `IAiPipeline`, provenance records that enumerate active adapters and models, admin surface entries that echo configuration values with their source, telemetry fan-out via `AiCallTelemetry`.
- **Error Modes:** Missing baseline provider yields a single bootstrap warning plus admin banner; adapter handshakes that fail capability negotiation mark the model inactive without breaking the pipeline; provisioning delays return deferred responses while the pipeline streams progress events.
- **Acceptance Criteria:** `AddKoan()` lights up a functioning `IAiPipeline` without extra configuration, Microsoft.Extensions.AI middlewares execute in the Koan pipeline order, admin surfaces show the same configuration values emitted by the pipeline, and adapters can be swapped through standard configuration or registrar contributions.

**Edge Cases**

- Empty configuration: Koan.AI still supplies a default local adapter (Ollama or emulator) and surfaces the bootstrap warning in admin.
- Remote adapters unreachable: pipeline emits `DeferredAiResponse` with telemetry breadcrumbs; admin shows adapter health as degraded.
- Capability mismatch (chat model missing embeddings): model flagged inactive; registry falls back to next compatible model.
- Provisioning with large model downloads: progress surfaced through pipeline status stream to avoid frontend timeouts.
- Custom tenant overrides: registrar honours tenant-specific `Koan:Ai:Sources:*` sections before applying global defaults.

## Context

Koan.AI currently maintains a bespoke execution pipeline, custom adapters, and duplicated telemetry plumbing alongside Microsoft.Extensions.AI (ME.AI). The duplication blocks us from adopting ME.AI middleware (safety filters, content shaping) and makes zero-configuration onboarding impossible—new Koan projects must hand-wire adapters, routes, and provenance emitters. Administrators also lack a single view of AI configuration: Koan.Admin exposes some settings, while Koan.AI reports others in bespoke boot notes.

The Microsoft.Extensions.AI stack now ships the primitives we need—client factories, pipelines, safety middleware. We must realign Koan.AI as a thin, Koan-opinionated layer on top of ME.AI, keep the zero-config promise in `AddKoan()`, and surface configuration consistently through Koan.Admin using the provenance pattern defined in `Koan.Admin.Initialization.KoanAdminAutoRegistrar`.

## Decision

- **Adopt ME.AI pipeline primitives:** Replace the bespoke Koan.AI dispatcher with an `IAiPipeline` instance built on `IServiceCollection.AddAiPipeline(...)` from Microsoft.Extensions.AI. Koan-specific enrichers (session state, Koan context injection, provenance stamping) become ME.AI middleware registered through the same pipeline builder to maintain order and composability.
- **Ship an auto-registrar:** Introduce `Koan.Ai.Initialization.KoanAiAutoRegistrar` so `AddKoan()` discovers and installs the AI pipeline without manual wiring. The registrar:
  - Registers the default conversational and embedding pipelines, pulling configuration via `Koan.Core.Configuration.Read` to honour standard overrides.
  - Discovers adapters contributed through `IAiAdapterContributor` (OpenAI, Ollama, LM Studio, Azure OpenAI) and wires them into ME.AI `IAIModelClientFactory` registrations.
  - Emits boot provenance using `ProvenanceModuleExtensions.PublishConfigValue` and typed `KoanAiProvenanceItems`, mirroring the pattern in `KoanAdminAutoRegistrar` so admin surfaces show value, source, and effective model roster.
  - Supplies adapter-backed chat and embedding clients (`AdapterBackedChatClient`, `AdapterBackedEmbeddingGenerator`) that use a shared `AiRoutingEngine`; the legacy `IAiRouter` surface is removed altogether.
    - Surfaces explicit `Koan:Ai:Sources` declarations and legacy `Koan:Ai:Ollama` fallbacks in provenance so operators can trace the effective routing catalogue without scanning configuration files.
  - Schedules `AiProvenancePublisher` as a hosted service to publish live adapter capabilities and source/member health snapshots into provenance once the host is running.
- **Zero-config defaults:** When no configuration is provided, the registrar enables an `OllamaLocalAdapter` (if available) or a lightweight emulator and queues provisioning via `IAiModelProvisioner`. Provisioning progress flows through ME.AI pipeline status notifications so callers receive a deferred response and administrators see download progress.
- **Unified telemetry:** Route all pipeline telemetry through `AiCallTelemetry` implementations that write to Koan provenance streams and Application Insights exporters selected by hosting. ME.AI middleware entries (content filters, redaction) append events to the same correlation id so per-call trails stay intact.
- **Admin alignment:** The AI registrar extends the Koan.Admin module definition to:
  - Display every `Koan:Ai:*` value along with `ConfigurationSource` metadata.
  - Surface capability tables per adapter/model pair (chat/embeddings/audio) and flag degraded health.
  - Provide quick actions (re-provision, disable model) by delegating to `IAiAdapterMaintenanceService` endpoints exposed through the controller layer.
- **Extensibility contracts:** Document new interfaces—`IAiAdapterContributor`, `IAiPipelineDecorator`, `IAiModelProvisioner`—under `Koan.Ai`. Adapters must declare capabilities and optional provisioning steps; decorators may append ME.AI middleware while preserving the base pipeline order. All contracts live in Koan.Ai to avoid leaking ME.AI internals to application code.

## Consequences

- **Positive:**
  - ME.AI middleware (safety, logging, retries) is now available everywhere Koan.AI runs.
  - New projects obtain a functional AI pipeline via `AddKoan()` with no extra code.
  - Admin receives a single, source-aware view of AI configuration and model health.
  - Adapter authors implement small contributors instead of duplicating pipelines.
- **Negative / Trade-offs:**
  - Koan.AI now depends on ME.AI abstractions; we must track their API changes closely.
  - Provisioning progress introduces asynchronous response patterns (deferred responses) that existing synchronous tests must adapt to.
  - Additional coordination required to keep Koan provenance schemas aligned with ME.AI telemetry payloads.

## Implementation Notes

1. Add `KoanAiAutoRegistrar` that composes ME.AI pipeline registrations and exports provenance following `KoanAdminAutoRegistrar` patterns.
2. Build out default adapters (OpenAI, Azure OpenAI, Ollama, emulator) as `IAiAdapterContributor` implementations that wrap ME.AI clients.
3. Introduce provisioning status stream (`IAiModelProvisioner`) and connect it to pipeline deferred responses plus admin UI.
4. Finish removing legacy router call paths so everything flows through `IAiPipeline` + `AiRoutingEngine` inside Koan.AI.
5. Update documentation (`docs/guides/data/all-query-streaming-and-pager.md`, Koan.AI reference) to showcase first-class static usage and streaming guidance.
6. Extend Koan.Admin to display the new AI settings and capability matrix.

## Migration Notes

- Sweep for any lingering `IAiRouter` references in applications and migrate them to `IAiPipeline` (the router surface no longer ships).
- Move adapter registrations out of `Startup`/`Program` and into assembly-level contributors implementing `IAiAdapterContributor`.
- Adopt new admin module contributions so configuration values render with their source metadata; existing handcrafted admin tiles should be retired.
- Regression-test samples (`samples/S5.Recs`, `samples/S13.DocMind`) against deferred provisioning flows and capability reporting.

## References

- `src/Koan.Admin/Initialization/KoanAdminAutoRegistrar.cs` (configuration provenance pattern)
- AI-0008 AI adapters and registry
- AI-0010 Entrypoint and augmentations
- ARCH-0065 AddKoan bootstrap lives in Koan.Core
- ARCH-0044 Standardized module config and discovery
