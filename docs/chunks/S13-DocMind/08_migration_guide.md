### **8. Success Criteria & Acceptance Testing**

#### **Functional Requirements Checklist**
- [ ] **Document Upload**: Support `.txt`, `.pdf`, `.docx`, and common image formats up to 50 MB (default configuration) with duplicate detection.
- [ ] **Text Extraction**: Demonstrate reliable PDF/DOCX extraction and document any image/OCR fallbacks.
- [ ] **Insight Generation**: Produce narrative summaries via `InsightSynthesisService` and record fallbacks when the model defers.
- [ ] **Template System**: Generate templates through `TemplateSuggestionService` and allow prompt-test runs before acceptance.
- [ ] **Embedding Assist (optional)**: When Weaviate is present, persist embeddings and confirm graceful degradation when absent.
- [ ] **Timeline Diagnostics**: Persist `DocumentProcessingEvent` records for each stage and expose them via `/api/documents/{id}/timeline` + `/api/processing/queue`.
- [ ] **Manual Analysis**: Enable ad-hoc insight runs via `AnalysisController` endpoints.
- [ ] **MCP Exposure**: Confirm `[McpEntity]` resources for documents, insights, and templates are discoverable over the HTTP SSE transport.
- [ ] **Auto-Registration**: Boot sample with a single `AddKoan()` call and the shipped `DocMindRegistrar`.
- [ ] **API Generation**: Expose CRUD + workflow endpoints with pagination and filtering aligned to the Angular client.

#### **Performance Requirements Checklist**
- [ ] **Throughput**: Process at least one standard sample document per minute on developer hardware (stretch: parallel batch via `WorkerBatchSize` tuning).
- [ ] **Concurrency**: Sustain two simultaneous uploads without starving the background worker (stretch: validate with `MaxConcurrency = 4`).
- [ ] **Response Time**: CRUD API responses under 500 ms (excluding AI work); health endpoints under 200 ms.
- [ ] **Processing Duration**: Document analysis completes within 90 s for sample inputs, with timelines showing each stage timestamp.
- [ ] **Memory Usage**: Keep API container below 1.5 GB RSS during demos.
- [ ] **Startup Time**: Application ready in under 20 s on a developer laptop.

#### **Security & Compliance Checklist**
- [ ] **Data Storage**: Document storage path isolated per environment with clear cleanup scripts (`docmind-reset`).
- [ ] **Audit Trail**: `DocumentProcessingEvent` entries retained for the life of the workshop dataset.
- [ ] **Input Validation**: Enforce file size/content-type checks and document any manual review for untrusted inputs.
- [ ] **Access Model**: Document the sample's anonymous defaults and outline steps to enable auth when required.
- [ ] **Operational Hygiene**: Provide reset scripts and instructions for purging data between workshops.

#### **Observability & Cost Checklist**
- [ ] **Boot Snapshot**: Capture Koan boot report output for each environment and store with deployment artifacts.
- [ ] **Processing Timeline Export**: Save recent `DocumentProcessingEvent` data (queue + timeline endpoints) as part of diagnostics bundles.
- [ ] **Logging**: Ensure structured logs include document IDs and stage names for correlation.
- [ ] **Model Usage Awareness**: Document how to inspect `ModelsController` usage stats; no automated budgeting is required for the sample scope.
- [ ] **Workshop Runbook**: Maintain a lightweight runbook covering reset steps, model install commands, and known limits.

### **9. Migration & Rollback Procedures**

#### **Data Migration Strategy**
```csharp
namespace S13.DocMind.Migration
{
    public class LegacyDataMigrator
    {
        public async Task<MigrationResult> MigrateFromLegacyAsync(
            string legacyConnectionString,
            MigrationOptions options,
            CancellationToken ct = default)
        {
            var result = new MigrationResult();

            // Phase 1: Extract legacy documents
            var legacyDocuments = await ExtractLegacyDocuments(legacyConnectionString);
            result.TotalDocuments = legacyDocuments.Count;

            // Phase 2: Transform to Koan entities
            var transformedDocuments = new List<Document>();
            foreach (var legacyDoc in legacyDocuments)
            {
                try
                {
                    var document = TransformLegacyDocument(legacyDoc);
                    transformedDocuments.Add(document);
                    result.TransformedDocuments++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to transform document {legacyDoc.Id}: {ex.Message}");
                }
            }

            // Phase 3: Bulk insert into Koan
            await Document.BulkUpsert(transformedDocuments);
            result.MigratedDocuments = transformedDocuments.Count;

            // Phase 4: Verify migration
            if (options.VerifyMigration)
            {
                result.VerificationPassed = await VerifyMigrationAsync(legacyDocuments, transformedDocuments);
            }

            return result;
        }
    }

    public class MigrationResult
    {
        public int TotalDocuments { get; set; }
        public int TransformedDocuments { get; set; }
        public int MigratedDocuments { get; set; }
        public bool VerificationPassed { get; set; }
        public List<string> Errors { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }
}
```

### **10. Code Migration Mappings & Reusable Components**

#### **Original → Target Component Mapping**

| **Original Component** | **S13.DocMind Target** | **Migration Strategy** | **Reusable Code** |
|------------------------|------------------------|------------------------|-------------------|
| `GDoc.Api.Models.UploadedDocument` | `S13.DocMind.Models.File` | Convert to Entity<T>, add extraction state tracking | Property definitions, validation logic |
| `GDoc.Api.Models.DocumentTypeConfiguration` | `S13.DocMind.Models.Type` | Convert to Entity<T>, add AI extraction prompts | Template structure, validation rules |
| `GDoc.Api.Models.DocumentationRequest` | `S13.DocMind.Models.Analysis` | Restructure as AI analysis result entity | Context processing logic |
| `GDoc.Api.Services.DocumentProcessingService` | `S13.DocMind.Services.FileAnalysisService` | Replace with user-driven type assignment | Text extraction methods |
| `GDoc.Api.Services.LlmService` | Built-in Koan AI interface | Remove custom HTTP client code | Prompt building logic |
| `GDoc.Api.Repositories.*Repository` | Automatic via Entity<T> patterns | Remove repository classes | Query logic for custom endpoints |
| `GDoc.Api.Controllers.*Controller` | `EntityController<T>` inheritance | Replace manual CRUD with inheritance | Business logic endpoints |
| `Program.cs` DI registration | `DocMindRegistrar` | Ensure shipped registrar is loaded | Service configuration logic |

#### **Code Harvesting Guide for Agentic AI**

##### **1. Text Extraction Logic (High Reuse Potential)**
```csharp
// Original: GDoc.Api.Services.Document.FileTextExtractionService
// Location: references/gdoc/src/GDoc.Api/Services/Document/FileTextExtractionService.cs
// Reusable: PDF, DOCX, image processing methods

// HARVEST THIS CODE:
public async Task<string> ExtractTextFromPdfAsync(Stream pdfStream)
{
    using var document = PdfDocument.Open(pdfStream);
    var text = new StringBuilder();

    foreach (var page in document.GetPages())
    {
        text.AppendLine(page.Text);
    }

    return text.ToString();
}

// ADAPT TO S13.DocMind:
namespace S13.DocMind.Services
{
    public class TextExtractionService
    {
        // Copy PDF extraction logic with minimal changes
        public async Task<string> ExtractTextFromPdfAsync(Stream pdfStream)
        {
            // Reuse original implementation
        }
    }
}
```

##### **2. Document Type Templates (High Reuse Potential)**
```csharp
// Original: GDoc.Api.Models.DocumentTypeConfiguration
// Location: references/gdoc/src/GDoc.Api/Models/DocumentTypeConfiguration.cs
// Reusable: Template structure, tag normalization, validation

// HARVEST THIS CODE:
public static List<string> NormalizeTags(List<string>? tags)
{
    if (tags == null || tags.Count == 0) return new List<string>();

    return tags
        .Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Select(tag => tag.Trim().ToLowerInvariant().Replace(" ", "-"))
        .Where(tag => !string.IsNullOrEmpty(tag))
        .Distinct()
        .ToList();
}

// ADAPT TO S13.DocMind:
// Move to DocumentTemplate entity as static method
public sealed class DocumentTemplate : Entity<DocumentTemplate>
{
    // ... properties ...

    public static List<string> NormalizeTags(List<string>? tags)
    {
        // Copy exact implementation from original
    }
}
```

##### **3. File Processing Workflows (Medium Reuse Potential)**
```csharp
// Original: GDoc.Api.Services.DocumentProcessingService
// Location: references/gdoc/src/GDoc.Api/Services/DocumentProcessingService.cs
// Reusable: File validation, hash computation, deduplication logic

// HARVEST THIS CODE:
public bool IsSupportedFileType(string fileName)
{
    var supportedExtensions = new[] { ".txt", ".pdf", ".docx", ".png", ".jpg", ".jpeg" };
    var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
    return supportedExtensions.Contains(extension);
}

// ADAPT TO S13.DocMind:
namespace S13.DocMind.Services
{
    public class DocumentValidationService
    {
        // Copy with additional file type support
        public bool IsSupportedFileType(string fileName)
        {
            // Extend original logic
            var supportedExtensions = new[] { ".txt", ".pdf", ".docx", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
            // ... rest of implementation
        }
    }
}
```

##### **4. AI Prompt Building (High Reuse Potential)**
```csharp
// Original: GDoc.Api.Services.LlmService.GenerateDocumentTypeAsync
// Location: references/gdoc/src/GDoc.Api/Services/LlmService.cs
// Reusable: Prompt templates, JSON parsing, retry logic

// HARVEST THIS CODE:
private const string DocTypeJsonStart = "---DOCUMENT_TYPE_JSON_START---";
private const string DocTypeJsonEnd = "---DOCUMENT_TYPE_JSON_END---";

private string ExtractDelimitedJson(string content, string startDelimiter, string endDelimiter)
{
    var startIndex = content.IndexOf(startDelimiter);
    var endIndex = content.IndexOf(endDelimiter);

    if (startIndex == -1 || endIndex == -1 || endIndex <= startIndex)
        return string.Empty;

    startIndex += startDelimiter.Length;
    return content.Substring(startIndex, endIndex - startIndex).Trim();
}

// ADAPT TO S13.DocMind:
namespace S13.DocMind.Services
{
    public class PromptParsingService
    {
        // Copy delimiter constants and extraction methods
        private const string JSON_START = "---JSON_START---";
        private const string JSON_END = "---JSON_END---";

        // Reuse parsing logic with Koan AI integration
        public T ParseJsonResponse<T>(string aiResponse) where T : class
        {
            var json = ExtractDelimitedJson(aiResponse, JSON_START, JSON_END);
            return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Failed to parse AI response");
        }
    }
}
```

##### **5. Event Processing Patterns (Medium Reuse Potential)**
```csharp
// Original: GDoc.Api.Services.DocumentProcessingService processing workflow
// Location: references/gdoc/src/GDoc.Api/Services/DocumentProcessingService.cs
// Reusable: Processing stage logic, error handling, status tracking

// HARVEST THIS CODE:
public async Task<OllamaResponse> ProcessSingleDocumentAsync(
    Guid documentId,
    string instructions,
    string template,
    string? notes,
    string requestId,
    bool forceReextraction)
{
    var documentIds = new List<Guid> { documentId };
    return await GenerateFromExtractionsAsync(documentIds, instructions, template, notes, requestId, forceReextraction);
}

// ADAPT TO S13.DocMind:
namespace S13.DocMind.Services
{
    public class DocumentProcessingOrchestrator
    {
        // Transform to event-sourced pattern
        public async Task ProcessDocumentAsync(Guid documentId, ProcessingOptions? options = null)
        {
            // Convert linear processing to event-driven workflow
            await RecordEvent(documentId, ProcessingStage.Started, ProcessingState.Processing);

            try
            {
                // Reuse processing logic with Koan AI integration
                var document = await Document.Get(documentId);
                var analysis = await _intelligenceService.AnalyzeDocumentAsync(document);

                await RecordEvent(documentId, ProcessingStage.Completed, ProcessingState.Completed);
            }
            catch (Exception ex)
            {
                await RecordEvent(documentId, ProcessingStage.Failed, ProcessingState.Failed, ex.Message);
            }
        }
    }
}
```

#### **Database Migration Patterns**

##### **MongoDB Document Transformation**
```csharp
// Original MongoDB collections → Koan Entity mapping
namespace S13.DocMind.Migration
{
    public class MongoDocumentMigrator
    {
        public Document TransformUploadedDocument(BsonDocument originalDoc)
        {
            return new Document
            {
                // Direct property mappings
                FileName = originalDoc["fileName"].AsString,
                UserFileName = originalDoc.Contains("userFileName") ? originalDoc["userFileName"].AsString : null,
                ContentType = originalDoc["contentType"].AsString,
                FileSize = originalDoc["fileSize"].AsInt64,
                ExtractedText = originalDoc["content"].AsString,
                Sha512Hash = originalDoc["sha512Hash"].AsString,
                Notes = originalDoc.Contains("notes") ? originalDoc["notes"].AsString : "",

                // Transform processing state
                State = MapProcessingState(originalDoc),

                // Handle binary content - lift into the configured storage provider during migration
                StorageBucket = originalDoc.Contains("storageBucket")
                    ? originalDoc["storageBucket"].AsString
                    : "legacy-docs",
                StorageObjectKey = originalDoc.Contains("storageObjectKey")
                    ? originalDoc["storageObjectKey"].AsString
                    : $"legacy/{originalDoc["_id"].AsObjectId}.bin",
                StorageVersionId = originalDoc.Contains("storageVersionId")
                    ? originalDoc["storageVersionId"].AsString
                    : null,

                // Convert timestamps
                CreatedAt = originalDoc["uploadDate"].ToUniversalTime(),
                LastAnalyzed = originalDoc.Contains("lastExtractionDate")
                    ? originalDoc["lastExtractionDate"].ToUniversalTime()
                    : null
            };
        }

        private ProcessingState MapProcessingState(BsonDocument doc)
        {
            var isExtractionComplete = doc.Contains("isExtractionComplete") && doc["isExtractionComplete"].AsBoolean;
            var hasError = doc.Contains("processingError") && !string.IsNullOrEmpty(doc["processingError"].AsString);

            if (hasError) return ProcessingState.Failed;
            if (isExtractionComplete) return ProcessingState.Completed;
            return ProcessingState.Uploaded;
        }
    }
}
```

##### **Service Registration Migration**
```csharp
// Original: Manual DI registration in Program.cs (60+ lines)
// Target: DocMindRegistrar pattern

// MIGRATE FROM:
// builder.Services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();
// builder.Services.AddScoped<UploadedDocumentRepository>();
// builder.Services.AddSingleton<LlmService>();
// ... (58 more lines)

// MIGRATE TO:
// Provided by DocMind package; ensure assembly is referenced.
public sealed class DocMindRegistrar : IKoanAutoRegistrar
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // Only register business services - data access is automatic
        services.AddScoped<DocumentIntelligenceService>();
        services.AddScoped<DocumentProcessingOrchestrator>();
        services.AddScoped<TextExtractionService>();
        services.AddScoped<PromptParsingService>();

        // Background services
        services.AddHostedService<DocumentProcessingBackgroundService>();
    }
}
```

#### **API Endpoint Migration Patterns**

##### **Controller Transformation Guide**
```csharp
// Original: Manual CRUD implementation
// Target: EntityController inheritance

// MIGRATE FROM:
[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly UploadedDocumentRepository _documentRepository;

    [HttpGet]
    public async Task<ActionResult<List<UploadedDocument>>> GetAll()
    {
        var documents = await _documentRepository.GetAllAsync();
        return Ok(documents.OrderBy(d => d.FileName).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UploadedDocument>> GetById(Guid id)
    {
        var document = await _documentRepository.GetByIdAsync(id);
        return document != null ? Ok(document) : NotFound();
    }

    // ... 15 more manual CRUD methods
}

// MIGRATE TO:
[Route("api/documents")]
public class DocumentController : EntityController<Document>
{
    // All CRUD operations auto-generated
    // Only add custom business logic endpoints
    private readonly IObjectStorageClient _storage;
    private readonly IBackgroundCommandBus _commandBus;

    [HttpPost("upload")]
    public async Task<ActionResult<Document>> Upload([FromForm] DocumentUploadRequest request)
    {
        // Reuse original upload logic with Koan entities
        foreach (var file in request.Files)
        {
            await using var sourceStream = file.OpenReadStream();
            await using var hashingStream = new HashingReadStream(sourceStream, SHA512.Create());

            var uploadResult = await _storage.UploadAsync(new ObjectUploadRequest
            {
                Bucket = "documents",
                ObjectName = $"{Guid.NewGuid():N}/{file.FileName}",
                ContentType = file.ContentType,
                Content = hashingStream
            });

            var hash = hashingStream.ComputeHashHex();
            var existing = await Document.Where(d => d.Sha512Hash == hash).FirstOrDefault();
            if (existing != null) return Ok(existing);

            var document = new Document
            {
                FileName = uploadResult.ObjectName,
                UserFileName = file.FileName,
                ContentType = file.ContentType,
                FileSize = file.Length,
                Sha512Hash = hash,
                StorageBucket = uploadResult.Bucket,
                StorageObjectKey = uploadResult.ObjectName,
                StorageVersionId = uploadResult.VersionId
            };

            await document.Save();
            await _commandBus.EnqueueAsync(new DocumentUploadedCommand
            {
                DocumentId = document.Id,
                Bucket = uploadResult.Bucket,
                ObjectKey = uploadResult.ObjectName
            });

            return CreatedAtAction(nameof(GetById), new { id = document.Id }, document);
        }

        return BadRequest();
    }
}
```

#### **Configuration Migration**

##### **Connection Strings & Provider Setup**
```yaml
# Original: Single MongoDB configuration
# Migrate FROM:
ConnectionStrings:
  MongoDB: "mongodb://mongodb:27017"
MongoDB:
  DatabaseName: "gdoc"

# Migrate TO: Multi-provider Koan configuration
Koan:
  Data:
    Providers:
      mongodb:
        connectionString: "mongodb://mongodb:27017"
        database: "s13docmind"
        priority: 5
      weaviate:
        endpoint: "http://weaviate:8080"
        priority: 7
```

#### **Code Reuse Checklist for Implementation**

##### **High Priority (90%+ Reusable)**
- [ ] **Text extraction methods** from `FileTextExtractionService.cs`
- [ ] **File validation logic** from `DocumentProcessingService.cs`
- [ ] **Hash computation** and deduplication algorithms
- [ ] **Tag normalization** methods from `DocumentTypesController.cs`
- [ ] **JSON parsing** and delimiter extraction from `LlmService.cs`
- [ ] **Prompt templates** for document analysis and template generation

##### **Medium Priority (60-80% Reusable with Adaptation)**
- [ ] **Processing workflow logic** (convert to event-sourced)
- [ ] **Error handling patterns** and retry mechanisms
- [ ] **File upload handling** (adapt to Entity<T> patterns)
- [ ] **Template matching algorithms** (enhance with vector similarity)
- [ ] **Configuration validation** and initialization
- [ ] **Health check implementations**

##### **Low Priority (30-50% Reusable - Patterns Only)**
- [ ] **MongoDB repository patterns** (replace with Entity<T>)
- [ ] **Manual DI registration** (confirm DocMindRegistrar wiring)
- [ ] **Custom HTTP clients** (replace with Koan AI)
- [ ] **Manual CRUD controllers** (replace with EntityController<T>)
- [ ] **Custom event handling** (replace with Flow entities)

#### **Implementation Priority Matrix**

| **Migration Phase** | **Original Components** | **Target Implementation** | **Reuse Strategy** |
|-------------------|------------------------|--------------------------|-------------------|
| **Phase 1: Core Entities** | Models/* | Entity<T> definitions | Copy properties, add Koan attributes |
| **Phase 2: Data Access** | Repositories/* | Remove (auto-generated) | Extract custom query logic only |
| **Phase 3: Business Logic** | Services/* | Koan-integrated services | Reuse algorithms, replace infrastructure |
| **Phase 4: APIs** | Controllers/* | EntityController<T> | Keep business endpoints, remove CRUD |
| **Phase 5: Infrastructure** | Program.cs, configs | DocMindRegistrar | Verify registrar wiring |

This comprehensive mapping ensures agentic AI systems can systematically harvest and transform existing code while maximizing reuse and minimizing reimplementation effort.

### **11. Troubleshooting Guide**

#### **Common Issues & Solutions**
```markdown
# S13.DocMind Troubleshooting Guide

## Provider Connection Issues

### MongoDB Connection Failed
**Symptoms**: "Unable to connect to MongoDB" errors in logs
**Solutions**:
1. Verify connection string format: `mongodb://host:port/database`
2. Check MongoDB service status: `docker ps | grep mongo`
3. Validate network connectivity: `telnet mongo-host 27017`
4. Review MongoDB logs: `docker logs mongo-container`

### Weaviate Vector Operations Failing
**Symptoms**: Vector similarity searches return empty results
**Solutions**:
1. Verify Weaviate endpoint accessibility
2. Check vector dimensions match (1536 for OpenAI embeddings)
3. Validate vector index configuration
4. Review Weaviate schema setup

## AI Integration Issues

### Ollama Service Unavailable
**Symptoms**: AI.Prompt() calls timeout or fail
**Solutions**:
1. Check Ollama container status
2. Verify model is downloaded: `ollama list`
3. Test direct API access: `curl http://ollama:11434/api/version`
4. Review model memory requirements vs available resources

### OpenAI API Rate Limits
**Symptoms**: 429 rate limit errors in AI processing
**Solutions**:
1. Implement exponential backoff retry logic
2. Consider request batching for bulk operations
3. Monitor API usage in OpenAI dashboard
4. Implement request queuing for high-volume scenarios

## Performance Issues

### Slow Document Processing
**Symptoms**: Processing takes longer than 90-second target
**Solutions**:
1. Check available memory and CPU resources
2. Review document size (10 MB baseline; stretch goal 25 MB)
3. Validate AI model performance
4. Consider enabling in-memory caching for performance optimization
5. Monitor concurrent processing limits and Flow queue depth

### High Memory Usage
**Symptoms**: Application consuming >1.5 GB RAM during demos
**Solutions**:
1. Review large document handling
2. Implement streaming for file processing
3. Check for memory leaks in AI operations
4. Optimize entity caching strategies
```

**Implementation References:**
- **Core Entity Models**: `/samples/S13.DocMind/Models/`
- **AI Integration**: `/samples/S13.DocMind/Services/DocumentIntelligenceService.cs`
- **Flow Entities**: `/samples/S13.DocMind/Flows/`
- **API Controllers**: `/samples/S13.DocMind/Controllers/`
- **Container Orchestration**: `/samples/S13.DocMind/docker-compose.yml`
- **Migration Tools**: `/samples/S13.DocMind/Migration/`
- **Health Checks**: `/samples/S13.DocMind/Health/`
- **Integration Tests**: `/samples/S13.DocMind/Tests/Integration/`
=======
## **Migration & Adoption Guide**

### 1. Starting Point
- Current sample already runs via docker-compose with API, Angular client, MongoDB, Weaviate, and Ollama.
- API exposes legacy `/api/files/*` endpoints with minimal functionality; Angular calls expect richer contract.
- Goal: migrate to new domain models, pipeline, and APIs without breaking compose-based setup.

### 2. Incremental Migration Strategy

| Step | Description | Safeguards |
|------|-------------|------------|
| **1. Dual-write models** | Introduce new `SourceDocument` entity alongside existing `File` while controllers still serve legacy contract. | Wrap writes so both schemas stay in sync; add feature flag to opt into new pipeline. |
| **2. Background pipeline shadowing** | Run `DocumentAnalysisPipeline` in parallel but mark results as experimental; expose via new timeline endpoint for verification. | Keep legacy synchronous processing active until parity confirmed. |
| **3. Controller switchover** | Gradually replace `/api/files` routes with `/api/documents` equivalents; provide temporary proxy endpoints returning both old + new payloads. | Contract tests ensuring Angular + MCP clients handle new DTOs. |
| **4. UI update** | Flip Angular feature flag to use new services; remove legacy adapters. | Ship toggle for workshop demos to revert if needed. |
| **5. Clean-up** | Delete legacy entities/services once adoption complete. | Mongo migration script to drop old collections once confirmed empty. |

### 3. Data Migration Checklist
- [ ] Snapshot existing Mongo collections (`files`, `documenttypes`) before schema changes.
- [ ] Execute migration script creating `source_documents`, `semantic_type_profiles`, `document_chunks`, `document_insights`, `document_processing_events`.
- [ ] Backfill `Summary` field for existing documents using current analysis text (if available).
- [ ] Generate embeddings only when Weaviate reachable; otherwise leave null and schedule backfill job.
- [ ] Verify counts between old and new collections; document any records skipped with reasons.

### 4. Code Migration Cheat Sheet
- Replace `File` references with `SourceDocument` in server + client code; rename TypeScript interfaces accordingly.
- Swap `FileAnalysisService` for `DocumentAnalysisPipeline` orchestrated flow; controllers call dispatcher instead of service directly.
- Migrate `DocumentTypesController.Generate` to `TemplatesController.Generate` using new prompt structures.
- Update Angular API services to `DocumentsApiClient`, `TemplatesApiClient`, `InsightsApiClient`, `ModelsApiClient` generated from OpenAPI.
- Update `Program.cs` to call `AddDocMind()` and remove manual DI registrations.

### 5. Rollback Plan
- Keep legacy controllers/services behind feature flag `DocMind:Features:LegacyApi` until new stack stable.
- Provide script `scripts/docmind-rollback.sh` that restores Mongo backup, reverts configuration, and restarts compose stack.
- Maintain documentation for enabling/disabling new Angular feature toggles (`environment.ts`).

### 6. Communication & Documentation
- Update README quick-start to highlight new endpoints, timeline feature, and replay tooling.
- Provide migration FAQ detailing new terminology (SourceDocument vs File, SemanticTypeProfile vs Type).
- Document manual verification steps (upload sample, verify timeline, accept template suggestion, run MCP tool).

### 7. Acceptance Criteria
- ✅ New endpoints deliver same or better data compared to legacy ones; Angular works without console errors.
- ✅ Pipeline handles supported formats (PDF, DOCX, TXT, PNG) within documented SLAs.
- ✅ Timeline and insights visible in UI, Postman, and MCP clients.
- ✅ Compose stack remains single-command setup; optional overrides documented.
- ✅ Legacy code removed or feature-flagged with clear deprecation timeline.

This migration guide ensures teams can transition from the existing minimal implementation to the fully realized DocMind refactor while maintaining stability for demos and workshops.