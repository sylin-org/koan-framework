# S13.DocMind Gap Assessment & Rebuild Roadmap

## 1. Current Implementation Snapshot (2025-02 audit)
The existing S13.DocMind API compiles and boots correctly, connects to MongoDB and Weaviate via the provided Docker Compose script, and serves the Angular client. Beyond that baseline, the implementation remains a thin CRUD sample without the promised document-intelligence experiences.

### 1.1 API Surface
- `FilesController` exposes only auto-generated CRUD plus lightweight helpers (stats, status, assign type) and still contains TODOs where analysis triggers should live; there is no upload endpoint, background orchestration, or chunk metadata publishing.
- `DocumentTypesController` returns fabricated AI generation results and lacks similarity search or evaluation routes; document type seeding scans the entire collection on each call.
- `AnalysisController` is limited to CRUD-style retrieval without summarization, aggregation, or regeneration paths.

### 1.2 Domain & Persistence
- Entities are generic `Entity<T>` records (`File`, `DocumentType`, `Analysis`) with strings, dictionaries, and status flags—no dedicated value objects for storage locations, prompts, or insight payloads.
- There is no representation of document chunks, insight collections, processing events, or embeddings; Weaviate integration is unused.
- Files store a `FilePath` but no abstraction for storage providers or deduplication.

### 1.3 Processing & AI Services
- The codebase contains zero AI service integrations; extraction, classification, insight generation, chunking, and embedding operations are entirely absent.
- No hosted/background workers exist. Status transitions are set synchronously inside the controller and never progress past "assigned" in practice.
- Prompt templates, model routing, and provider selection are not implemented.

### 1.4 Infrastructure & DX
- `Program.cs` configures `AddKoan()` with observability only; there is no auto-registrar, options binding, or boot diagnostics for AI/data providers.
- Compose scripts boot MongoDB, Weaviate, and Ollama successfully, but the application ignores Ollama and Weaviate because no services call them.
- Developer tooling (OpenAPI, TypeScript clients, MCP) is absent despite the documentation claiming full support.

## 2. Gap Summary vs Proposal
| Capability Pillar | Proposal Expectation | Current Reality | Gap Impact |
| --- | --- | --- | --- |
| **Ingestion & Storage** | Streaming upload endpoint, storage abstraction, dedupe, chunk capture | CRUD-only controller with no upload or storage integration | Cannot ingest documents or persist raw content safely |
| **Extraction & Enrichment** | Multi-format extraction (PDF/DOCX/OCR), metadata projection, chunk insights | No extraction logic or pipelines; `ExtractedText` stays empty | Users receive zero insights and no chunked context |
| **Type Intelligence** | Auto-classification, AI-generated templates, semantic similarity suggestions | Manual type assignment and mock generator | No guidance for users to choose templates; Weaviate unused |
| **Analysis Pipeline** | Background pipeline with retries, telemetry, and reprocessing | Inline status changes and TODO comments | Long-running operations block UI and never complete |
| **Search & Discovery** | Vector-backed similarity, analytics endpoints, filtering | Not implemented; controllers return raw lists | Client UI calls fail; sample demo paths broken |
| **Model Management** | API to enumerate/install models, configure providers, show health | No controllers or services touching models | Cannot demonstrate Koan AI provider abstraction |
| **MCP & Automation** | Tools/resources enabling DocMind workflow via MCP | No MCP packages or configuration | Proposal promise of agent orchestration unmet |
| **Developer Experience** | Auto-registrar, boot report, Postman scripts, OpenAPI clients | Basic README; no tooling or diagnostics | Onboarding friction, poor observability |

## 3. Break-and-Rebuild Strategy
With the infrastructure baseline proven, the rest of the implementation should be treated as a greenfield rebuild that replaces the current CRUD shell entirely. The strategy below sequences the work from bedrock (domain models) to UI and automation, ensuring each layer exposes intent-driven, Koan-aligned capabilities.

### Phase A – Domain Bedrock
1. **Introduce purposeful models**: Replace `File`/`DocumentType`/`Analysis` with `SourceDocument`, `SemanticTypeProfile`, `DocumentChunk`, `DocumentInsight`, `DocumentProcessingEvent`, and `DocumentTemplateDraft`. Each model expresses intent through strong types (e.g., `StorageLocation`, `PromptTemplate`, `EmbeddingVector`).
2. **Establish persistence contracts**: Configure MongoDB collections via Koan data attributes, add indexes for status, type, and uploaded timestamps, and define optional Weaviate schemas for embeddings. Migrate legacy collections or drop them for a clean slate depending on workshop needs.
3. **Codify configuration**: Create `DocMindOptions` binding sections for storage limits, concurrency, and default models so other layers consume consistent configuration.

### Phase B – Processing Spine
1. **Document Intake Service**: Implement a streaming upload endpoint backed by an injectable storage provider (local file system by default) that registers `SourceDocument` records, performs dedupe hashing, and emits intake events.
2. **Extraction Stage**: Build `TextExtractionService` and `VisionExtractionService` leveraging Koan AI abstractions to extract text and OCR diagrams; persist chunk boundaries and raw text snapshots.
3. **Insight & Template Stage**: Create `InsightSynthesisService` and `TemplateSuggestionService` that transform extracted text into structured insights and semantic type recommendations using embeddings + prompts.
4. **Background Orchestration**: Add a bounded-channel `DocumentAnalysisPipeline` hosted service. It sequences intake → extraction → insight → suggestion, updates `DocumentProcessingEvent` logs, and surfaces metrics/health checks.
5. **Reprocessing Hooks**: Allow replay/regeneration with idempotent commands that push documents back onto the pipeline with updated settings.

### Phase C – Application Surface
1. **Scenario-focused controllers**: Replace the existing controllers with `DocumentsController`, `TemplatesController`, `InsightsController`, and `ProcessingController` that expose cohesive workflows (upload, status, replay, suggestion, diagnostics) using DTOs that match UI needs.
2. **Search & Similarity endpoints**: Implement endpoints for chunk search, similar documents/types, and analytics dashboards by querying MongoDB and Weaviate through Koan query helpers.
3. **Model management API**: Provide `/api/models` routes that enumerate installed/available models, toggle active providers, and surface health results from Koan AI clients.
4. **MCP enablement**: Reference Koan MCP packages, configure `EnableMcp`, and publish tools/resources mirroring the API commands (upload, analyze, fetch insights).

### Phase D – Experience Layer
1. **Angular alignment**: Regenerate OpenAPI/TypeScript clients, update services/components to the new DTOs, and add UI for chunk timelines, insight cards, and template suggestions.
2. **Diagnostics & DX**: Expose boot reports, health checks, and a developer dashboard summarizing pipeline backlog, recent errors, and provider availability. Ship Postman collections and scripted demo flows.
3. **Automation & Testing**: Author integration tests that exercise upload → completion, template generation, and similarity search using the compose stack; include smoke scripts in CI.

## 4. Guiding Principles
- **Minimal stack first**: Keep Docker Compose limited to API + MongoDB + Weaviate + Ollama; additional providers remain optional adapters.
- **Koan-first abstractions**: Favor Koan `Entity<T>`, AI clients, and configuration helpers to minimize custom plumbing.
- **Intent-driven naming**: Ensure classes communicate purpose (`DocumentInsightTimeline`, `SemanticTypeSuggestion`) for clarity.
- **Observability baked-in**: Every pipeline stage records structured events and exposes metrics for workshop demos.
- **Agent parity**: Treat MCP tools, HTTP APIs, and UI actions as different faces of the same services to avoid divergent logic.

## 5. Next Steps Checklist
1. Confirm compose stack health and capture baseline logs.
2. Scaffold new domain project and entities; remove legacy models/controllers once replacements compile.
3. Implement intake + storage services with tests before wiring the pipeline.
4. Layer pipeline services and hosted worker, validating with integration tests against MongoDB/Weaviate stubs.
5. Replace API surface and regenerate UI/clients.
6. Enable MCP tooling, author diagnostics, and finalize documentation/demo scripts.
