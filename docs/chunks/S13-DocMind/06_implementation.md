## **Implementation Roadmap**

### 1. Phased Delivery Plan

| Phase | Focus | Key Outcomes |
|-------|-------|--------------|
| **0. Baseline Audit** | Confirm compose stack, run existing app smoke tests | Document current gaps, capture API/UI mismatches, ensure telemetry logging works. |
| **1. Domain Foundation** | Introduce new entities/value objects, migrations | `SourceDocument`, `SemanticTypeProfile`, `DocumentChunk`, `DocumentInsight`, `DocumentProcessingEvent` live with seed data + updated TypeScript clients. |
| **2. Background Pipeline** | Implement channel-based `DocumentAnalysisPipeline` | Hosted service orchestrates extraction → insights → suggestions with retry + telemetry. |
| **3. API / MCP Alignment** | Replace controllers, surface DTOs, wire MCP tools | Controllers match UI expectations, MCP tools share contracts, OpenAPI regenerated. |
| **4. UI & DX Polish** | Update Angular flows, add diagnostics, finalize docs | Upload wizard, insight views, timeline, Postman/CLI assets, README refresh. |

Each phase completes with a demo script and validation checklist so workshops can stop mid-way if needed.

### 2. Detailed Work Breakdown

#### Phase 0 – Baseline Audit
- Run `docker compose up` and validate API reaches MongoDB/Weaviate/Ollama using existing smoke script.
- Capture current API responses to inventory differences vs. Angular expectations.
- Configure logging to ensure future stages emit structured logs (`Serilog` or Koan defaults).

#### Phase 1 – Domain Foundation
- Add new domain project (`S13.DocMind.Domain`) containing entities, enums, and value objects.
- Create migration/seed script that maps legacy `files` collection → `source_documents` and populates summary fields.
- Update Angular models + API clients using `dotnet koan client` to align with new DTOs.
- Write unit tests for value objects (e.g., `DocumentProcessingSummary` ensures unique insight references).

#### Phase 2 – Background Pipeline
- Implement `DocumentIntakeService`, `DocumentAnalysisPipeline`, and supporting services as detailed in chunk 03.
- Introduce `DocumentProcessingOptions` and configuration binding.
- Add integration tests using in-memory storage + Koan AI test double to exercise entire pipeline.
- Ensure `DocumentProcessingEvent` entries created per stage; expose metrics via boot report.

#### Phase 3 – API & MCP Alignment
- Replace old controllers with scenario-centric controllers; ensure minimal code in `Program.cs`.
- Generate OpenAPI specification and TypeScript clients; update Angular services.
- Implement MCP tools/resources, verifying they invoke the same services (no duplicate logic).
- Add CLI commands (e.g., `dotnet run -- replay`) hooking into `ProcessingController` endpoints.

#### Phase 4 – UI & DX Polish
- Update Angular upload wizard, detail view, template gallery, and diagnostics dashboard.
- Add Postman collection + README instructions for new endpoints.
- Finalize documentation (this chunk set) to reflect final architecture and operations guidance.

### 3. Coding Standards & Best Practices
- **SoC**: Keep controllers thin, services focus on single responsibility, domain models stay persistence-agnostic except for Koan attributes.
- **Koan-first**: Prefer Koan-provided abstractions (`Entity<T>`, `IAiClient`, `IStorageClient`, `AddKoan()`) over custom plumbing.
- **Telemetry**: Use structured logging with message templates, persist processing events, expose metrics via OpenTelemetry.
- **Naming clarity**: Use verbs for commands/services (`TemplateGeneratorService.GenerateAsync`) and nouns for models/DTOs.
- **Configuration**: All tunables (queue size, retry counts, models) live under `DocMind` configuration sections with sensible defaults.

### 4. Quality Gates
- **Unit Tests**: Domain model behaviors, prompt builders, pipeline stage retries.
- **Integration Tests**: Upload → completion flow using stubbed providers, verifying timeline events and insights.
- **Contract Tests**: Ensure OpenAPI and MCP schemas remain synchronized (CI job diffing generated specs).
- **Performance Smoke**: Process sample pack of 20 documents under 5 minutes on developer hardware.
- **Security Review**: Validate upload limits, MIME type checks, and ensure sensitive data not logged.

### 5. Risks & Mitigations
- **Model rename churn** → Mitigate via automated TypeScript regeneration and `Obsolete` attributes on legacy APIs for transition.
- **AI provider availability** → Provide test doubles and fallback prompts; document manual model installation steps.
- **Queue overload** → Bounded channel with backpressure + user-facing message when queue full.
- **Weaviate optionality** → Detect provider presence at boot; disable embedding stage gracefully.

### 6. Success Criteria
- Uploading PDF/DOCX/PNG triggers full pipeline, resulting in insights, chunk data, and template suggestions within UI.
- Timeline view reflects real-time status transitions from `DocumentProcessingEvent` data.
- Templates can be generated, tested, and assigned via API, UI, and MCP tools with matching results.
- Docker Compose stack remains a single command setup; docs explain toggling advanced features.
- Developers can replay documents, inspect diagnostics, and iterate prompts using provided tooling.

This roadmap connects the new domain models, background pipeline, API surface, and UI refresh into a coherent refactor plan aligned with Koan capability integration principles.
