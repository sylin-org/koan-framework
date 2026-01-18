# Koan.AI Microsoft.Extensions.AI Rearchitecture Tracker

**Anchor ADR:** [AI-0019](../../decisions/AI-0019-koan-ai-meai-zero-config.md)

## Scope

Track the multi-iteration work to align Koan.AI with Microsoft.Extensions.AI while preserving zero-config defaults, provenance, and admin visibility.

## Workstreams

- [ ] Auto-registrar implementation
  - [x] `KoanAiAutoRegistrar` emits provenance for `Koan:Ai` toggles, configured sources, and legacy Ollama fallbacks.
  - [x] Adapter-backed chat/embedding clients registered; `IAiRouter` removed in favour of `AiRoutingEngine` and `AdapterBacked*` clients.
  - [x] Live adapter/source snapshots published via `AiProvenancePublisher` so Admin surfaces reflect health.
- [ ] Pipeline abstraction swap
  - [x] Retired `IAiRouter`; `AiRoutingEngine` now serves request election behind the Microsoft.Extensions.AI pipeline.
- [ ] Adapter contributor conversions
  - [x] LM Studio discovery registers through `IAiAdapterContributor`, replacing the hosted-service bootstrap.
  - [x] Ollama discovery ports to `IAiAdapterContributor` with config-based fallbacks.
  - Port OpenAI, Azure OpenAI, Ollama, and emulator adapters onto `IAiAdapterContributor` and ME.AI client factories.
- [ ] Provisioning and deferred responses
  - Implement `IAiModelProvisioner`, surface progress through ME.AI status streams, and adapt samples/tests to deferred responses.
- [ ] Telemetry unification
  - Route middleware events through the shared `AiCallTelemetry` pipeline and confirm provenance correlation across exporters.
- [ ] Koan.Admin surfacing
  - Extend the admin module to show AI settings, capability matrices, and remediation tools driven by the new registrar output.
- [ ] Documentation updates
  - Refresh Koan.AI guides, samples, and data-access references to reflect ME.AI pipelines and zero-config behaviour.

## Dependencies & Notes

- Aligns with `ARCH-0065` (`AddKoan` bootstrap) and `ARCH-0044` (standardized config discovery).
- Coordinate telemetry schema updates with the provenance team to avoid breaking Application Insights exporters.
- Samples `S5.Recs` and `S13.DocMind` provide regression coverage for embeddings and conversational flows.

## Next Check-in

Target an implementation status review once the auto-registrar and first adapter conversion land (expected within the next milestone cycle).
