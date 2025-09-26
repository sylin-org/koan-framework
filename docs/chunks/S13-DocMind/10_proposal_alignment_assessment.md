# S13.DocMind Proposal vs. Current Sample Assessment

## 1. Proposal Intent
- **Purpose**: Deliver a “DocMind” showcase that proves Koan’s promise of turning reference documentation into runnable intent, reaching near–Google Docs feature parity for document intelligence workflows.
- **Audience**: Teams evaluating Koan as an end-to-end platform—covering data modeling, AI orchestration, MCP automation, and UI alignment through a single cohesive sample.
- **Guiding Scope**: Support workshop-sized datasets (dozens of documents up to ~10 MB) while demonstrating scalable architecture patterns that production teams can extend.

## 2. Promised Capability Pillars
1. **Ingestion & Storage** – Streaming uploads with dedupe, typed metadata, chunk capture, and storage-provider abstraction.
2. **Extraction & Enrichment** – Multi-format parsing (PDF, DOCX, images), structured insights, prompt-driven summaries, and diagram understanding routed through Koan AI clients.
3. **Semantic Typing & Similarity** – Auto-generated templates, classification confidence, and vector-backed similarity searches against MongoDB + optional Weaviate.
4. **Background Processing Spine** – Hosted worker (channel queue) sequencing intake → extraction → insight generation with retries, telemetry, and replay hooks—*without* MQ or Koan.Flow dependencies.
5. **Experience Surfaces** – Scenario-centric HTTP APIs, Angular UI parity, and Model Context Protocol (MCP) tools exposing the same commands for agent workflows.
6. **Operations & DX** – Docker Compose bootstrap, Koan auto-registrars, boot diagnostics, model management endpoints, and workshop-ready test scripts.

## 3. Koan Capability Demonstrations
- **AddKoan Simplicity**: Single call lights up observability, data adapters, AI providers, and MCP transport—with extra services auto-registered via a `AddDocMind()` extension.
- **Entity-First Design**: Rich `SourceDocument`, `SemanticTypeProfile`, `DocumentChunk`, `DocumentInsight`, and `DocumentProcessingEvent` entities unlock CRUD + workflow endpoints with minimal boilerplate.
- **AI Abstractions**: Koan AI prompt, extraction, and embedding APIs unify Ollama, OpenAI, or Azure OpenAI while honoring model installation/status contracts exposed by the sample.
- **MCP Integration**: Shared tooling ensures agents can trigger uploads, regenerate insights, or retrieve suggestions using the same background pipeline as the UI.
- **Composable Infrastructure**: Minimal Compose stack (API, MongoDB, Weaviate, Ollama) demonstrates provider flexibility while keeping setup approachable.

## 4. Current Sample Reality (2025-02 Audit)
- API boots successfully with Compose and reaches MongoDB/Weaviate, but exposes only scaffolded CRUD endpoints plus TODO markers for core workflows.
- No upload, storage abstraction, extraction services, or background worker exist; status fields never progress beyond manual assignments.
- Document type “generation” returns fabricated data; similarity searches, chunk insights, and analytics endpoints are missing entirely.
- MCP packages and model management APIs are absent; Angular client calls into non-existent routes, leaving the UI broken beyond basic lists.
- Compose stack launches Ollama and Weaviate, yet the application never invokes them—undermining the advertised multi-provider narrative.

## 5. Strengths of the Proposal
- **End-to-End Storytelling**: Connects domain modeling, AI workflows, MCP tooling, and UI in a single blueprint, clearly communicating Koan’s differentiators.
- **DX Focus**: Emphasizes auto-registration, boot diagnostics, scripted demos, and incremental refactor phases—making the sample attractive for workshops.
- **Scalable Patterns**: Encourages value objects, background events, and vector-backed search that production teams can evolve beyond the minimal stack.

## 6. Key Gaps & Risks in Current Implementation
1. **Feature Parity Void** – Without intake, extraction, or insight services, the “DocMind” experience is effectively non-existent; UI routes fail or display placeholders.
2. **Semantic Intelligence Missing** – Lack of embeddings, similarity endpoints, and AI-backed template generation prevents demonstrating Koan’s semantic capabilities.
3. **Operational Blind Spots** – No boot report, health checks, or model management APIs; Compose services run blindly with no validation feedback.
4. **Agent Story Broken** – MCP tooling and shared commands are absent, so the “reference equals intent” promise for agents cannot be showcased.

## 7. Minimal-Stack Alignment & Opportunities
- **Compose-first Delivery**: Retain the existing Docker Compose scripts as the happy path; ensure new services (storage abstraction, background worker, AI clients) plug into that baseline without extra brokers.
- **Hosted Worker over Flow**: Implement the channel-backed `DocumentAnalysisPipeline` worker to respect the no-MQ/no-Flow constraint while still modeling retries and telemetry.
- **Provider Gateways**: Introduce adapters that detect Weaviate/Ollama availability at boot, log capability readiness, and degrade gracefully when optional services are offline.
- **UI/Agent Contract Harmonization**: Derive Angular service contracts and MCP tool schemas from the same OpenAPI spec to eliminate divergence.

## 8. Recommendations Before Refactor Execution
- Socialize this assessment with stakeholders to confirm the flagship scope is still desired given current implementation debt.
- Prioritize scaffolding the domain models and background worker before revisiting UI—ensuring Compose health checks validate the end-to-end pipeline early.
- Document acceptance tests that walk through upload → insight generation → similarity search so the rebuilt sample can prove parity quickly.

## 9. Conclusion
The S13.DocMind proposal remains a compelling showcase of Koan’s capabilities, but the existing implementation falls dramatically short. Treat the refactor as a greenfield rebuild atop the working Compose baseline, anchoring every step in Koan’s minimal-stack integration principles to finally deliver the promised document-intelligence experience.