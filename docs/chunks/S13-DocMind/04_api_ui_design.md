## **API, UI, and MCP Refactoring Plan**

### 1. API Surface – Scenario-Centric Controllers
We retain Koan’s `EntityController<T>` base but wrap it with intentful controllers that map to core user journeys. Each controller exposes CRUD plus workflow endpoints backed by the refactored services.

| Controller | Purpose | Key Endpoints | Notes |
|------------|---------|---------------|-------|
| `DocumentsController` (`EntityController<SourceDocument>`) | Upload, status, insight retrieval | `POST /api/documents/upload`, `POST /api/documents/{id}/assign-profile`, `GET /api/documents/{id}/timeline`, `GET /api/documents/{id}/chunks` | Upload endpoint streams file, returns `DocumentUploadReceipt`, and enqueues pipeline work. |
| `TemplatesController` (`EntityController<SemanticTypeProfile>`) | Manage profiles, generate new templates, prompt testing | `POST /api/templates/generate`, `POST /api/templates/{id}/prompt-test` | Uses `TemplateSuggestionService` + Koan AI to create new templates and run test prompts. |
| `InsightsController` (`EntityController<DocumentInsight>`) | Surface structured insights and aggregated collections | `GET /api/documents/{id}/insights`, `GET /api/insights/collections/{profileId}` | Provides filtered, paginated insight feeds for UI dashboards. |
| `ProcessingController` | Diagnostics & admin | `POST /api/processing/replay`, `POST /api/processing/retry`, `GET /api/processing/config` | Bridges CLI/MCP automation with hosted pipeline configuration. |
| `ModelsController` | Provider status and installation | `GET /api/models`, `POST /api/models/install`, `GET /api/models/providers` | Mirrors Angular client expectations and surfaces Koan AI provider telemetry. |

**Design goals**
- All controllers use request/response DTOs (`UploadDocumentRequest`, `TimelineEntryResponse`) to decouple UI from EF-like entities.
- Query endpoints leverage Koan data filtering (`QueryRequest<T>`) with friendly filters (e.g., `status:Completed`, `profile:MEETING`).
- MCP tools reuse controller DTO contracts to keep a single source of truth for schema.

### 2. Upload & Status Workflow

```csharp
[HttpPost("upload")]
[RequestSizeLimit(20_000_000)]
public async Task<ActionResult<DocumentUploadReceipt>> UploadAsync(
    [FromForm] UploadDocumentRequest request,
    CancellationToken cancellationToken)
{
    var receipt = await _documentIntake.UploadAsync(request, cancellationToken);
    return CreatedAtAction(nameof(GetByIdAsync), new { id = receipt.DocumentId }, receipt);
}
```

`DocumentIntakeService.UploadAsync` responsibilities:
1. Validate MIME type & size using Koan validation rules.
2. Stream file to configured storage provider.
3. Create `SourceDocument` with `Status = Uploaded` and `Summary.TextExtracted = false`.
4. Record a `DocumentProcessingEvent` (stage `Upload`).
5. Enqueue `DocumentWorkItem` on the channel queue.
6. Return `DocumentUploadReceipt` containing document ID, file name, and initial status.

`GET /api/documents/{id}/timeline` returns ordered `DocumentProcessingEvent` entries, enabling the UI to render progress bars.

### 3. Assigning Profiles & Triggering Analysis
- `POST /api/documents/{id}/assign-profile` accepts `AssignProfileRequest { Guid ProfileId, bool AcceptSuggestion }`.
- Service updates `SourceDocument.AssignedProfileId`, sets `AssignedBySystem` accordingly, records processing event `Stage = Deduplicate` with context, and re-enqueues document if analysis not yet run.
- MCP tool `docmind.assign-profile` mirrors the endpoint for agent-driven workflows.

### 4. Insights & Chunk Retrieval
- `GET /api/documents/{id}/chunks` supports `includeInsights=true` to join chunk metadata with insights.
- `GET /api/documents/{id}/insights` provides pagination, channel filtering (`?channel=Vision`), and search (`?query=threat`).
- `GET /api/insights/collections/{profileId}` returns aggregated dashboards built by `InsightAggregationService`.

### 5. Template Management & Generation

```csharp
[HttpPost("generate")]
public async Task<ActionResult<SemanticTypeProfileResponse>> GenerateAsync(
    [FromBody] TemplateGenerationRequest request,
    CancellationToken cancellationToken)
{
    var profile = await _templateGenerator.GenerateAsync(request, cancellationToken);
    return CreatedAtAction(nameof(GetByIdAsync), new { id = profile.Id }, profile.ToResponse());
}
```

- `TemplateGenerationRequest` captures user prompt + target output shape.
- `TemplateGeneratorService` composes prompts from curated examples, calls `AI.Prompt`, parses JSON, and persists `SemanticTypeProfile` with embeddings.
- `POST /api/templates/{id}/prompt-test` executes a test prompt using `InsightSynthesisService` over user-provided text, returning the raw model response alongside parsed insights.

### 6. Model Management APIs
- `GET /api/models` lists installed models with provider metadata and health.
- `POST /api/models/install` triggers asynchronous installation for Ollama models by delegating to a hosted installer service (reusing the background queue pattern but separate channel).
- Endpoints surface Koan AI provider diagnostics (rate limits, default model) to match Angular UI expectations.

### 7. Angular UI Alignment

**Key UI flows to update:**
- **Upload wizard**: Display `DocumentUploadReceipt`, poll `/timeline` until `Completed`, show stage statuses (Uploaded → Extracting → Analyzing → Completed).
- **Document detail page**: Render chunk list with lazy insight expansion, highlight `PrimaryFindings`, present suggestion chips for template matches.
- **Template gallery**: Leverage new `TemplatesController` responses to show description, tags, and extraction schema. Provide “test prompt” modal to preview structured output.
- **Processing diagnostics dashboard**: Table of recent `DocumentProcessingEvent` items across documents, with filters for failures.

**Component naming alignment:**
- Rename Angular services and models to match new domain terms (`SourceDocument`, `SemanticTypeProfile`, `DocumentProcessingEvent`).
- Generate TypeScript clients from OpenAPI to ensure DTO consistency.

### 8. MCP Integration
- Expose MCP tools that wrap the same services:
  - `docmind.list-documents`
  - `docmind.upload`
  - `docmind.assign-profile`
  - `docmind.timeline`
  - `docmind.generate-template`
- Resources: `docmind/document/{id}`, `docmind/template/{id}`, `docmind/insight/{id}`.
- Each tool delegates to controller endpoints, preserving minimal implementation cost while demonstrating Koan MCP integration.

### 9. Developer Experience Optimizations
- Consolidate service registration via `S13DocMindRegistrar` that maps configuration sections (`Processing`, `Storage`, `Vision`, `Embedding`).
- Document CLI commands (`dotnet run --project samples/S13.DocMind -- replay --document <id>`) for replays and prompt experiments.
- Provide Postman collection / REST Client file mirroring new endpoints for quick verification.

This API/UI plan completes the refactor by aligning controller responsibilities, UI flows, and MCP tooling with the new domain models and background pipeline.
