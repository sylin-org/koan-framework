# S13.DocMind Scope Reduction Opportunities

## Evaluation Approach
- Reviewed every S13-DocMind documentation chunk to catalog promised capabilities and operational expectations.
- Compared those claims against the runnable sample in `samples/S13.DocMind` by tracing background processing, diagnostics surfaces, AI flows, manual analysis, and MCP configuration.
- Highlighted areas where documentation can contract to the features actually delivered so the proposal centers on executable scope.

## Observability & Diagnostics
- **What the code does:** `DocumentProcessingWorker` records each pipeline transition through `DocumentProcessingEvent` entries while coordinating extraction, embeddings, vision, and synthesis in a single background loop; when nothing is queued it simply sleeps, with no dependency on external telemetry stacks.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L36-L124】
- **Boot report coverage:** `DocMindRegistrar` injects diagnostics (vector health, discovery stats, storage validation) directly into the Koan boot report, so teams already have a built-in readiness view without Grafana/OpenTelemetry workstreams.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L123】
- **Trim opportunity:** Replace exporter/dashboard requirements with guidance that leans on the event timeline plus boot report output, and treat any additional observability targets as optional backlog items.

## MCP Integration
- **What the code does:** The only transport wired today is HTTP SSE, enabled through `EnableHttpSseTransport` in `appsettings.json`; there is no STDIO process runner or multi-channel handshake to maintain.【F:samples/S13.DocMind/appsettings.json†L9-L13】
- **Entity exposure:** Document and insight entities are decorated with `[McpEntity]`, which is sufficient for resource discovery over the SSE endpoint without extra tooling layers.【F:samples/S13.DocMind/Models/SourceDocument.cs†L9-L67】
- **Trim opportunity:** Reframe MCP scope around the running SSE surface and drop promises about STDIO orchestration, cross-surface tooling, or Aspire integration.

## Vision & Insight Generation
- **Vision reality:** `VisionInsightService` first emits image diagnostics, then falls back to a deterministic metadata narrative whenever AI vision is unavailable, so documentation should not imply diagram parsing or advanced risk models out of the box.【F:samples/S13.DocMind/Services/VisionInsightService.cs†L68-L199】
- **Insight synthesis:** `InsightSynthesisService` attempts an AI prompt when available, but always produces chunk highlights and a fallback summary for resilience, demonstrating that deep analytics dashboards are not required to claim success.【F:samples/S13.DocMind/Services/InsightSynthesisService.cs†L32-L174】
- **Trim opportunity:** Limit commitments to the ingestion → chunking → insight loop with graceful fallbacks, and list diagram semantics, risk lenses, or analytics drill-downs as stretch goals.

## Manual Analysis & Diagnostics APIs
- **Existing tooling:** `ManualAnalysisService` already exposes reusable sessions, prompt overrides, and run telemetry without extra orchestration components, suggesting the docs can focus on showcasing this workflow instead of inventing new reporting suites.【F:samples/S13.DocMind/Services/ManualAnalysisService.cs†L15-L163】
- **Diagnostics endpoints:** `ProcessingController` provides queue, timeline, replay, and discovery validation endpoints that pair with the event ledger, covering the operational insights promised in the refined deliverable.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L10-L112】
- **Trim opportunity:** Emphasize these built-in surfaces and remove redundant dashboard/reporting deliverables so the plan highlights existing Koan-style diagnostics.

## Recommended Documentation Actions
1. Update observability and testing sections to reference `DocumentProcessingEvent` timelines, boot report snapshots, and the diagnostics controller instead of external telemetry systems.
2. Scale back MCP sections to the HTTP SSE configuration and entity annotations that currently ship, while listing STDIO tooling as backlog if desired.
3. Reword capability narratives to underline the actual AI fallbacks and manual analysis flows, keeping advanced analytics, diagram intelligence, and Aspire-style integrations out of the primary scope.
