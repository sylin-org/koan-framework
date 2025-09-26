### **8. Success Criteria & Acceptance Testing**

#### **Functional Requirements Checklist**
- [ ] **Document Upload**: Support .txt, .pdf, .docx, and image formats up to 10 MB each (stretch: 25 MB with streaming enabled).
- [ ] **Text Extraction**: Demonstrate ≥95 % accuracy on the curated sample pack; document gaps for edge formats.
- [ ] **AI Analysis**: Produce structured summaries with confidence scoring and human-review routing.
- [ ] **Template System**: Generate templates via AI and persist review decisions.
- [ ] **Vector Search**: Return top-5 similar templates in <2 s across the 200-document demo corpus.
- [ ] **Event Sourcing**: Persist a complete audit trail of upload → analysis Flow events.
- [ ] **Multi-Provider (core)**: Operate across MongoDB, Weaviate, and Ollama with simplified infrastructure stack.
- [ ] **Auto-Registration**: Boot sample with a single `AddKoan()` call plus provider packages.
- [ ] **API Generation**: Expose CRUD APIs with pagination, filtering, and relationship expansion.

#### **Performance Requirements Checklist**
- [ ] **Throughput**: Process 4 documents per minute in sequential demo runs (stretch: 20 with optional load script).
- [ ] **Concurrency**: Support 3 concurrent users in the base environment (stretch: 15 with scaled resources).
- [ ] **Response Time**: CRUD API responses under 500 ms (excluding AI work); health endpoints under 200 ms.
- [ ] **AI Processing**: Document analysis completes within 90 s for sample inputs.
- [ ] **Memory Usage**: Keep API container below 1.5 GB RSS during demos.
- [ ] **Startup Time**: Application ready in under 20 s on a developer laptop.

#### **Security Requirements Checklist**
- [ ] **Data Encryption**: All sensitive content encrypted at rest (filesystem volume encryption by default, object storage SSE when enabled) plus field-level encryption for secrets.
- [ ] **Audit Logging**: Complete audit trail for all user actions with exportable lineage reports.
- [ ] **Sensitive Data Classification**: Automated PII/PHI detection with redaction prior to AI prompts.
- [ ] **Retention & Erasure**: Lifecycle policies enforced and `RightToBeForgottenFlow` validated end-to-end.
- [ ] **Human Review Controls**: Low-confidence outputs held for manual approval before release.
- [ ] **Input Validation**: Comprehensive validation and antivirus scanning preventing malicious uploads.
- [ ] **Rate Limiting**: API rate limiting to prevent abuse.
- [ ] **Authentication**: Support for OAuth 2.0 and JWT tokens.
- [ ] **Authorization**: Role-based access control for documents and templates.

#### **Observability & Cost Checklist**
- [ ] **Tracing**: Distributed traces stitched across upload, Flow worker, and AI calls using OpenTelemetry.
- [ ] **Metrics**: Dashboards covering queue depth, stage latency, embedding throughput, and AI token spend.
- [ ] **Logging**: Structured logs with document IDs, review outcomes, and cost annotations.
- [ ] **Budgets & Alerts**: `AIUsageBudget` thresholds enforced with alerting and automatic model downgrades when exceeded.
- [ ] **SLO Reviews**: Weekly review ritual evaluating success metrics vs targets, with action items captured in runbook.

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