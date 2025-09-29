# **S13.DocMind: AI-Native Document Intelligence Platform**

## **Executive Summary**

**S13.DocMind** is a guided sample that demonstrates how the Koan Framework stitches together data and AI capabilities to build an AI-native document intelligence experience focused on ingestion, staged processing, and actionable insights. The deliverable is intentionally scoped to the features that ship in the repository today: streaming uploads, queued background processing, chunk-level analysis, insight synthesis with graceful fallbacks, and timeline diagnostics backed by persisted processing events. Rather than promising a feature-for-feature clone of productivity suites, it walks readers through the concrete architectural patterns and building blocks they can reuse when crafting document intelligence solutions inside Koan.

This sample assumes lightweight evaluation datasets (dozens of documents, individual files ≤10 MB) and is optimized for interactive walkthroughs, scripted demos, and workshop labs. Larger workloads, multi-team governance, and production-grade SLAs are explicitly positioned as future extensions so workshop content can stay focused on the working baseline.

The current experience highlights ingestion → chunking → AI insight generation, optional embedding enrichment, and HTTP SSE-based MCP exposure for entity data. It demonstrates core Koan Framework patterns for multi-provider data strategies, AI integration, auto-generated APIs, and event-backed diagnostics while maintaining the framework's "Reference = Intent" posture.

### **Transformation Overview**

| **Aspect** | **Original Solution** | **S13.DocMind (Enhanced)** |
|------------|-------------------|---------------------------|
| **Architecture** | Traditional .NET with manual DI | Entity-first with DocMind registrar bootstrap |
| **Data Layer** | MongoDB-only, repository pattern | Multi-provider patterns (MongoDB + optional Weaviate) |
| **AI Integration** | Manual Ollama client | Built-in `AI.Prompt()`, `AI.Embed()`, and `AI.VisionPrompt()` |
| **APIs** | Manual controller implementation | Auto-generated via `EntityController<T>` with rich enrichments |
| **Document Analysis** | Basic string extraction | Chunked extraction with summary synthesis and fallback messaging |
| **Visual Analysis** | Limited image processing | Image metadata capture with optional vision prompt fallbacks |
| **Type System** | Manual type assignment | Suggestion service with semantic heuristics and embedding assist when available |
| **User Experience** | Basic file metadata | Timeline, insight views, and queue diagnostics aligned to Angular client |
| **Large Files** | Single-pass processing | Document chunking with aggregated status metadata |
| **Search & Discovery** | Simple filename search | Content-based filtering via chunk summaries and optional embeddings |
| **Processing** | Synchronous with manual orchestration | Streamlined background processing with hosted services |
| **Scalability** | Single provider, container-aware | Multi-provider with graceful degradation hooks |
| **Developer Experience** | Complex setup, manual patterns | "Reference = Intent", zero configuration |
| **AI Agent Orchestration** | Manual API integration required | HTTP SSE MCP exposure for entity resources |

---

## **Problem Domain Analysis**

### **Document Intelligence Capabilities Demonstrated**
The refactored documentation now spotlights the capabilities that ship today:

**Core Processing:**
- **Multi-format Intake**: `.txt`, `.pdf`, `.docx`, and common image uploads routed through `DocumentIntakeService`
- **Chunked Extraction**: Paragraph-level chunking with token estimates to keep downstream prompts focused
- **Insight Synthesis**: `InsightSynthesisService` produces narrative summaries with safe fallbacks when the model declines
- **Timeline Tracking**: `DocumentProcessingEvent` timeline captures every stage for UI progress indicators and diagnostics

**AI-Powered Intelligence:**
- **Vision Metadata**: `VisionInsightService` records image dimensions and optional AI commentary when configured
- **Template Suggestions**: `TemplateSuggestionService` blends semantic heuristics with embeddings (when Weaviate is enabled)
- **Manual Analysis Support**: `ManualAnalysisService` enables ad-hoc insight runs and stats surfaced via `AnalysisController`

**User & Operator Experience:**
- **Angular Alignment**: Upload wizard, insight views, and diagnostics panels map directly to the controller contracts shipped
- **Processing Diagnostics**: `ProcessingController` exposes queue, timeline, replay, and config endpoints for workshops
- **Boot Report Visibility**: Koan boot report lists detected providers, registered hosted services, and MCP resources

**Extensibility Hooks:**
- **Embedding Optionality**: `[VectorAdapter("weaviate")]` entities activate automatically when the container is running
- **HTTP SSE MCP Exposure**: `[McpEntity]` attributes surface documents, insights, and templates to MCP clients over HTTP SSE
- **Prompt Reuse**: Templates store curated prompt fragments so the UI, services, and MCP layer share the same language

### **Architectural Challenges Identified**
1. **Manual Infrastructure**: 60+ lines of DI registration in `Program.cs`
2. **Provider Lock-in**: MongoDB-specific implementation patterns
3. **Complex Orchestration**: Manual service coordination and error handling (addressed through lightweight hosted services)
4. **Limited Scalability**: Single-provider architecture constrains growth
5. **AI Integration Complexity**: Custom HTTP clients and response parsing
6. **Development Friction**: Significant boilerplate for CRUD operations

### **Refactoring Vision**
- **Bedrock-first**: Stabilize the data foundation around clearly named models—`SourceDocument`, `DocumentTemplate`, `DocumentChunk`, `DocumentInsight`, and `SemanticTypeProfile`—so downstream services read as intent instead of plumbing.
- **Processing lanes with guardrails**: Center orchestration on the existing `DocumentProcessingWorker` + `DocumentProcessingJob` queue so ingestion, enrichment, embeddings, and insight synthesis all flow through one BackgroundService while publishing status projections through Koan observability primitives.
- **Composable AI services**: Centralize prompting, extraction, and embedding in focused collaborators (`TextExtractionService`, `InsightSynthesisService`, `TemplateSuggestionService`) that consume Koan AI abstractions directly and can be swapped for provider-specific implementations without touching controllers.
- **UI-aligned APIs**: Shape controllers around scenario-first endpoints (`DocumentsController`, `TemplatesController`, `InsightsController`, `ModelsController`) that pair naturally with the Angular client, limit chatty round-trips, and expose MCP tools with shared contracts.
- **Minimal-yet-extensible stack**: Keep Docker Compose as the happy path—API + MongoDB + Weaviate + Ollama—while providing clear switches for optional GPU, vector, or storage providers so workshops stay approachable.

### **Opportunities to Streamline Developer Experience**
- **Declarative service registration** via an `S13DocMindRegistrar` that auto-discovers pipeline services, storage providers, and hosted workers, shrinking `Program.cs` to intent-level configuration.
- **Self-describing diagnostics** using Koan boot reports plus per-stage processing metrics persisted on `DocumentProcessingEvent` to accelerate troubleshooting during labs.
- **Scenario-driven naming** throughout the solution (e.g., `DocumentIntakeController.UploadSourceDocument`) to clarify intent for newcomers and align with documentation.
- **Template-driven prompts** stored on the `SemanticTypeProfile` entity so AI agents, APIs, and MCP clients pull from a single source of truth.
- **Front-end ready contracts** that embed chunk summaries, insight collections, and suggestion payloads alongside status metadata to reduce bespoke mapping inside the Angular workspace.

---

