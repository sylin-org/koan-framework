# S13.DocMind Next Steps (Updated)

## Domain & Contract Alignment
1. **Carve out the domain assembly.** Create `S13.DocMind.Domain`, move entities/value objects from the sample project, and add bootstrap/migration routines that hydrate the new schema from existing `files` data per the proposal’s phase-one guidance.【F:docs/chunks/S13-DocMind/06_implementation.md†L22-L44】【F:samples/S13.DocMind/Models/SourceDocument.cs†L1-L189】
2. **Regenerate clients and MCP contracts.** Once DTOs stabilise, run `dotnet koan client` and regenerate MCP manifests so Angular, CLI, and MCP integrations share the new contracts, documenting breaking changes for workshop collateral.【F:docs/chunks/S13-DocMind/06_implementation.md†L34-L44】【F:samples/S13.DocMind/Controllers/DocumentsController.cs†L1-L138】

## Prompting Framework & Semantic Tooling
1. **Implement the GDoc-derived prompt builder.** Build a reusable prompt builder that encapsulates the delimiter patterns for document type generation and multi-document analysis outlined in the AI prompting guide, and apply it across template generation, insight synthesis, and future replay flows.【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L10-L173】【F:samples/S13.DocMind/Services/InsightSynthesisService.cs†L18-L340】
2. **Wire profiles to structured prompts.** Persist canonical prompts/extraction schemas on `SemanticTypeProfile`, surface prompt revisions, and refactor services to consume the builder instead of ad-hoc string concatenation so outputs remain schema-safe.【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L174-L209】【F:samples/S13.DocMind/Services/TemplateGeneratorService.cs†L1-L78】【F:samples/S13.DocMind/Models/SemanticTypeProfile.cs†L1-L82】

## Discovery Projections & Diagnostics
1. **Adopt incremental projections.** Replace full scans in `DocumentDiscoveryProjectionBuilder` with incremental `InsightCollection` aggregates, freshness windows, and cache invalidation aligned with the data blueprint.【F:docs/chunks/S13-DocMind/02_entity_models.md†L3-L122】【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L18-L212】
2. **Expose drift telemetry.** Extend diagnostics endpoints to compare cached projections with live counts/queue age so operators can spot backlog or staleness before dashboards degrade.【F:docs/chunks/S13-DocMind/01_executive_overview.md†L72-L95】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L54-L236】

## Observability & Operational Automation
1. **Enable OpenTelemetry traces/metrics.** Wrap worker stages, AI calls, and vector operations with spans/counters, wire exporters in the registrar, and document compose overrides for the otel collector per the infrastructure plan.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L60-L109】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L19-L115】
2. **Ship operational tooling.** Publish reset scripts, compose profiles, and environment variable matrices so teams can bootstrap, toggle features, or recover systems following the plan’s operational checklist.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L5-L32】【F:samples/S13.DocMind/docker-compose.yml†L1-L120】

## Testing & CI Guardrails
1. **Add end-to-end HTTP tests.** Use `WebApplicationFactory` to cover upload→completion flows, template suggestions, and diagnostics, including vector fallback scenarios, then run them in CI.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L82】【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L1-L88】
2. **Automate contract and smoke checks.** Generate/diff OpenAPI & MCP manifests, execute CLI replay smoke scripts, and gate PRs on these checks to satisfy the testing plan’s release guardrails.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L61-L110】【F:samples/S13.DocMind.Tools/Program.cs†L1-L142】

## Immediate Spikes
- **Migration blueprint:** Prototype the migration of existing Mongo collections into `SourceDocument`/`DocumentProcessingJob` without downtime, validating rollback steps.【F:docs/chunks/S13-DocMind/06_implementation.md†L22-L44】
- **Prompt verification harness:** Capture representative transcripts to validate the new prompt builder against the GDoc patterns before wiring it into the pipeline.【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L10-L209】
- **Telemetry exporter selection:** Experiment with Koan’s OpenTelemetry hooks to pick the OTLP/Prometheus exporters that align with the compose stack before documenting the setup.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L109】
