# S13.DocMind Proposal vs. Sample Alignment (Trimmed Scope)

## 1. Proposal Intent (Realigned)
- **Purpose**: Deliver a runnable Koan showcase that demonstrates ingestion → staged processing → insight delivery, backed by event timelines and optional embeddings, without promising Aspire-like observability or multi-transport MCP deployments.
- **Audience**: Teams exploring Koan’s entity-first patterns, background workers, AI integration, and MCP exposure within a workshop-sized footprint.
- **Guiding Scope**: Compose-first bootstrap (API + MongoDB + optional Weaviate + Ollama) processing dozens of documents ≤50 MB, with HTTP APIs, Angular parity, and HTTP SSE MCP resources.

## 2. Capability Pillars After Right-Sizing
1. **Ingestion & Storage** – Streaming uploads, duplicate detection, and storage abstraction via `IDocumentStorage`.
2. **Extraction & Insights** – PDF/DOCX extraction, optional vision metadata, chunk-level summaries, and manual analysis helpers.
3. **Semantic Typing** – Template suggestion service with embeddings when available, lexical fallbacks when not.
4. **Background Processing** – `DocumentProcessingWorker` orchestrating staged jobs with configurable concurrency/retries.
5. **Experience Surfaces** – Scenario-centric controllers, Angular clients, and HTTP SSE MCP exposure for shared DTOs.
6. **Operations & DX** – Auto-registrar bootstrap, boot report notes, processing diagnostics endpoints, and reset scripts.

## 3. Koan Capabilities Demonstrated
- **Auto-Registration Simplicity**: Single `AddKoan()` call with `DocMindRegistrar` wiring services, hosted workers, health checks, and boot report contributions.
- **Entity-First Design**: All domain concepts inherit from `Entity<T>`, unlocking CRUD + workflow endpoints with minimal boilerplate.
- **Provider Transparency**: Vector adapters activate automatically; when Weaviate is absent, fallbacks keep the flow alive.
- **AI Integration**: Koan AI abstractions (`AI.Prompt`, `AI.VisionPrompt`, `AI.Embed`) power insight synthesis, optional vision commentary, and embeddings.
- **MCP Integration**: `[McpEntity]` attributes surface resources over HTTP SSE (per `appsettings.json`), matching the documented scope.
- **Diagnostics**: `DocumentProcessingEvent` persistence, `ProcessingController` endpoints, and boot report notes supply the observability story.

## 4. Current Sample Reality (2025-02)
- **✅ Working Baseline**: Uploads, background processing, insights, template prompts/tests, manual analysis, and diagnostics operate as described.
- **✅ Diagnostics**: Queue/timeline/replay endpoints and boot report output align with documentation.
- **✅ MCP HTTP SSE**: Resources are discoverable and match DTOs referenced in the proposal.
- **⚠️ Vision Depth**: Image handling focuses on metadata + optional narrative prompts; no topology extraction—now accurately reflected in docs.
- **⚠️ Embedding Optionality**: Embeddings appear when Weaviate is present; docs treat this as optional with graceful fallback messaging.
- **⚠️ Authentication**: Sample defaults to anonymous usage; docs point readers to configuration rather than promising turnkey auth.

## 5. Residual Gaps & Actions
1. **Enhanced Vision Analytics** – Backlog if richer diagram understanding becomes a priority.
2. **MCP Tool Definitions** – Optional follow-up to wrap controller actions; reuse DTOs when implemented.
3. **Vector Troubleshooting** – Add README snippets for teams running without Weaviate (documented as a todo in docs).

## 6. Alignment Verdict
With the proposal trimmed, the sample now matches the documented commitments. Remaining differences are intentional backlog items, not hidden gaps. The deliverable credibly showcases Koan’s document intelligence patterns without overextending into unimplemented telemetry or transport promises.
