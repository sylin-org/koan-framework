## **Implementation Roadmap**

### 1. Phased Delivery Plan

| Phase | Focus | Key Outcomes |
|-------|-------|--------------|
| **0. Baseline Audit** | Confirm compose stack, run existing app smoke tests | Document current gaps, capture API/UI mismatches, confirm boot report + diagnostics endpoints surface data. |
| **1. Domain Foundation** | Introduce new entities/value objects, migrations | `SourceDocument`, `SemanticTypeProfile`, `DocumentChunk`, `DocumentInsight`, `DocumentProcessingEvent` live with seed data + updated TypeScript clients. |
| **2. Background Pipeline** | Harden `DocumentProcessingWorker` + job orchestration | Hosted service sequences extraction → insights → suggestions with retry + timeline recording. |
| **3. API / MCP Alignment** | Ensure controllers + DTOs match UI, surface MCP resources | Controllers match UI expectations, HTTP SSE resources reuse DTOs, OpenAPI regenerated. |
| **4. UI & DX Polish** | Update Angular flows, add diagnostics, finalize docs | Upload wizard, insight views, timeline, Postman/CLI assets, README refresh. |

Each phase completes with a demo script and validation checklist so workshops can stop mid-way if needed.

### 2. Detailed Work Breakdown

#### Phase 0 – Baseline Audit
- Run `docker compose up` and validate API reaches MongoDB/Weaviate/Ollama using existing smoke script.
- Capture current API responses to inventory differences vs. Angular expectations.
- Confirm boot report + `/api/processing/queue` timeline endpoints light up with the baseline processing sample.

#### Phase 1 – Domain Foundation
- Add new domain project (`S13.DocMind.Domain`) containing entities, enums, and value objects.
- Create migration/seed script that maps legacy `files` collection → `source_documents` and populates summary fields.
- Update Angular models + API clients using `dotnet koan client` to align with new DTOs.
- Write unit tests for value objects (e.g., `DocumentProcessingSummary` ensures unique insight references).

#### Phase 2 – Background Pipeline
- Keep `DocumentIntakeService`, `DocumentProcessingWorker`, and supporting services aligned with chunk 03 patterns.
- Introduce or refine `DocMindOptions` validation/binding for queue sizes, retries, and polling intervals.
- Add integration tests using in-memory storage + Koan AI test double to exercise entire pipeline and validate stage transitions.
- Ensure `DocumentProcessingEvent` entries created per stage and visible through diagnostics endpoints + boot report notes.

#### Phase 3 – API & MCP Alignment
- Keep controllers scenario-centric while holding `Program.cs` to the single `AddKoan()` call.
- Generate OpenAPI specification and TypeScript clients; update Angular services.
- Ensure MCP HTTP SSE resources reuse DTO contracts (tool definitions can remain backlog items called out in docs).
- Add CLI commands (e.g., `dotnet run -- replay`) hooking into `ProcessingController` endpoints.

#### Phase 4 – UI & DX Polish
- Update Angular upload wizard, detail view, template gallery, and diagnostics dashboard.
- Add Postman collection + README instructions for new endpoints.
- Finalize documentation (this chunk set) to reflect final architecture and operations guidance.

### 3. Coding Standards & Best Practices
- **SoC**: Keep controllers thin, services focus on single responsibility, domain models stay persistence-agnostic except for Koan attributes.
- **Koan-first**: Prefer Koan-provided abstractions (`Entity<T>`, `IAiClient`, `IStorageClient`, `AddKoan()`) over custom plumbing.
- **Telemetry**: Use structured logging with message templates, persist processing events, and surface diagnostics via boot report + processing endpoints.
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
