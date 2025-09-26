# **S13.DocMind: AI-Native Document Intelligence Platform**

## **Executive Summary**

**S13.DocMind** is a guided sample that showcases how the Koan Framework stitches together data, flow, and AI capabilities to build an AI-native document intelligence experience. Rather than prescribing an enterprise migration, it walks readers through the architectural patterns and building blocks they can reuse when crafting their own solutions.

This sample assumes lightweight evaluation datasets (dozens of documents, individual files ≤10 MB) and is optimized for interactive walkthroughs, scripted demos, and workshop labs. Larger workloads, multi-team governance, and production-grade SLAs are called out as optional explorations for teams who want to push the framework further.

### **Transformation Overview**

| **Aspect** | **Original Solution** | **S13.DocMind (Koan-Native)** |
|------------|-------------------|---------------------------|
| **Architecture** | Traditional .NET with manual DI | Entity-first with auto-registration |
| **Data Layer** | MongoDB-only, repository pattern | Sample multi-provider patterns (MongoDB + Weaviate core, PostgreSQL/Redis optional) |
| **AI Integration** | Manual Ollama client | Built-in `AI.Prompt()` and `AI.Embed()` with sample workflows |
| **APIs** | Manual controller implementation | Auto-generated via `EntityController<T>` |
| **Processing** | Synchronous with manual orchestration | Flow-driven background orchestration patterns |
| **Scalability** | Single provider, container-aware | Sample scaling hooks and stretch guidance |
| **Developer Experience** | Complex setup, manual patterns | "Reference = Intent", zero configuration |

---

## **Problem Domain Analysis**

### **Original Solution Capabilities**
The reference document intelligence solution provides sophisticated features:

- **Multi-format Processing**: .txt, .pdf, .docx, images with text extraction
- **AI-Powered Analysis**: Document information extraction and template filling
- **Template System**: Configurable document type templates with AI generation
- **Retrieval Pipeline**: Deterministic embeddings with experimental RAG capabilities
- **Diagram Understanding**: Graph extraction and visual content analysis
- **Generation Workflow**: Source documents → requests → runs → results pipeline

### **Architectural Challenges Identified**
1. **Manual Infrastructure**: 60+ lines of DI registration in `Program.cs`
2. **Provider Lock-in**: MongoDB-specific implementation patterns
3. **Complex Orchestration**: Manual service coordination and error handling
4. **Limited Scalability**: Single-provider architecture constrains growth
5. **AI Integration Complexity**: Custom HTTP clients and response parsing
6. **Development Friction**: Significant boilerplate for CRUD operations

---

## **S13.DocMind Architecture**

### **1. Entity-First Data Models**

#### **Core Document Entity**
```csharp
[DataAdapter("multi-provider")] // Demonstrates provider flexibility
public sealed class Document : Entity<Document>
{
    public string FileName { get; set; } = "";
    public string? UserFileName { get; set; }
    public string ContentType { get; set; } = "";
    public long FileSize { get; set; }

    // Content storage - leverages provider capabilities
    public string ExtractedText { get; set; } = "";
    public string? BinaryObjectKey { get; set; }
    public string? BinaryBucket { get; set; }
    public string Sha512Hash { get; set; } = "";

    // AI analysis results
    public ExtractedInformation? Analysis { get; set; }
    public DateTime? LastAnalyzed { get; set; }
    public ProcessingState State { get; set; } = ProcessingState.Uploaded;

    // Per-document notes for context override
    public string Notes { get; set; } = "";

    // Relationships - automatic navigation
    [Parent(typeof(DocumentTemplate))]
    public Guid? TemplateId { get; set; }

    // Vector capabilities - when Weaviate provider available
    [Vector(Dimensions = 1536)]
    public double[]? Embedding { get; set; }
}
```

The upload endpoint demonstrates Koan's ability to combine streaming ingestion, storage adapters, and background orchestration:

- **Streaming & hashing**: `HashingReadStream` wraps the incoming file stream, computing a SHA-512 digest as bytes flow to storage so large uploads never materialize fully in memory.
- **Pluggable storage client**: `IObjectStorageClient` defaults to the lightweight filesystem provider that writes into `./data/storage` so the sample runs without external services, while still allowing teams to switch to S3, Azure Blob, or MinIO when exploring advanced scenarios.
- **Durable orchestration**: `DocumentUploadedCommand` is queued on `IBackgroundCommandBus`, allowing Koan Flow workers to retry with exponential backoff, maintain idempotency, and absorb bursts without dropping work.
- **Security hooks**: Virus scanning and content classification run as part of the storage upload pipeline (see governance section) before metadata is committed.

```csharp
public sealed class HashingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly HashAlgorithm _hash;

    public HashingReadStream(Stream inner, HashAlgorithm hash)
    {
        _inner = inner;
        _hash = hash;
    }

    private bool _finalized;

    public string ComputeHashHex()
    {
        if (!_finalized)
        {
            _hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            _finalized = true;
        }
        return Convert.ToHexString(_hash.Hash ?? Array.Empty<byte>());
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytesRead = await _inner.ReadAsync(buffer, cancellationToken);
        if (bytesRead > 0)
        {
            _hash.TransformBlock(buffer.Span[..bytesRead], 0, bytesRead, null, 0);
        }
        return bytesRead;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _inner.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            _hash.TransformBlock(buffer, offset, bytesRead, null, 0);
        }
        return bytesRead;
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_finalized)
        {
            _hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            _finalized = true;
        }
        await base.DisposeAsync();
    }

    #region Stream forwarding members
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    #endregion
}
```

Key design notes:

- **Externalized binaries**: Only storage provider identifiers plus relative paths are persisted with the entity. Raw files live under the mounted filesystem root by default, keeping MongoDB lean and making it easy to clean or snapshot demo assets.
- **Streaming-friendly metadata**: Hashes, embeddings, and template associations are calculated without loading the whole document into process memory.
- **Vector fields remain optional**: When Weaviate is disabled, the Koan vector attribute is ignored, preserving compatibility with the minimal stack.

#### **AI-Enhanced Template System**
```csharp
public sealed class DocumentTemplate : Entity<DocumentTemplate>
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Instructions { get; set; } = "";
    public string Template { get; set; } = "";
    public List<string> Tags { get; set; } = new();

    // AI generation metadata
    public bool WasAiGenerated { get; set; }
    public string? GenerationPrompt { get; set; }
    public DateTime? LastRefined { get; set; }

    // Template matching capabilities
    [Vector(Dimensions = 1536)]
    public double[]? TemplateEmbedding { get; set; }

    // Child relationships - automatic via Koan
    public async Task<List<Document>> GetDocuments() => await GetChildren<Document>();
}
```

#### **Analysis Results**
```csharp
public sealed class DocumentAnalysis : Entity<DocumentAnalysis>
{
    [Parent(typeof(Document))]
    public Guid DocumentId { get; set; }

    public string Summary { get; set; } = "";
    public Dictionary<string, List<string>> Entities { get; set; } = new();
    public List<string> Topics { get; set; } = new();
    public List<KeyFact> KeyFacts { get; set; } = new();
    public double ConfidenceScore { get; set; }

    // Processing metadata
    public string ProcessingVersion { get; set; } = "";
    public TimeSpan ProcessingDuration { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### **2. AI-First Processing Architecture**

#### **Document Intelligence Service**
```csharp
public class DocumentIntelligenceService
{
    public async Task<DocumentAnalysis> AnalyzeDocumentAsync(Document document)
    {
        // Koan's unified AI interface - no HTTP client complexity
        var analysisPrompt = BuildAnalysisPrompt(document.ExtractedText);
        var result = await AI.Prompt(analysisPrompt);

        // Generate document embedding for similarity matching
        var embedding = await AI.Embed(document.ExtractedText);
        document.Embedding = embedding;
        await document.Save(); // Provider-transparent persistence

        return ParseAnalysisResult(result);
    }

    public async Task<List<DocumentTemplate>> FindSimilarTemplates(Document document)
    {
        if (document.Embedding == null) return new();

        // Koan's vector operations - automatic provider routing
        return await DocumentTemplate.Vector.SimilaritySearch(
            document.Embedding,
            threshold: 0.8,
            limit: 5
        );
    }

    public async Task<DocumentTemplate> GenerateTemplateAsync(string prompt)
    {
        var generationPrompt = $"""
            Create a document analysis template based on: {prompt}

            Output JSON format:
            {{
              "name": "Template Name",
              "instructions": "Analysis instructions...",
              "template": "# Template\n\n## Section\n{{PLACEHOLDER}}"
            }}
            """;

        var response = await AI.Prompt(generationPrompt);
        var templateData = JsonSerializer.Deserialize<TemplateData>(response);

        var template = new DocumentTemplate
        {
            Name = templateData.Name,
            Instructions = templateData.Instructions,
            Template = templateData.Template,
            WasAiGenerated = true,
            GenerationPrompt = prompt
        };

        // Generate template embedding for future matching
        template.TemplateEmbedding = await AI.Embed($"{template.Name} {template.Instructions}");

        return await template.Save(); // Auto GUID v7, provider-transparent
    }
}
```

### **3. Event-Sourced Processing Pipeline**

#### **Document Processing Flow Entities**
```csharp
public sealed class DocumentProcessingEvent : FlowEntity<DocumentProcessingEvent>
{
    public Guid DocumentId { get; set; }
    public ProcessingStage Stage { get; set; }
    public ProcessingState State { get; set; }
    public object? EventData { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan? Duration { get; set; }
}

public enum ProcessingStage
{
    Uploaded,
    TextExtracted,
    AIAnalysisStarted,
    AIAnalysisCompleted,
    TemplateMatched,
    VectorGenerated,
    ProcessingCompleted,
    ProcessingFailed
}

public sealed record DocumentUploadedCommand : FlowCommand
{
    public Guid DocumentId { get; init; }
    public string Bucket { get; init; } = string.Empty;
    public string ObjectKey { get; init; } = string.Empty;
}
```

#### **Processing Orchestrator with Event Sourcing**
```csharp
public class DocumentProcessingOrchestrator : FlowCommandHandler<DocumentUploadedCommand>
{
    private readonly DocumentIntelligenceService _intelligenceService;
    private readonly IObjectStorageClient _storage;
    private readonly ILogger<DocumentProcessingOrchestrator> _logger;

    public DocumentProcessingOrchestrator(
        DocumentIntelligenceService intelligenceService,
        IObjectStorageClient storage,
        ILogger<DocumentProcessingOrchestrator> logger)
    {
        _intelligenceService = intelligenceService;
        _storage = storage;
        _logger = logger;
    }

    public override async Task HandleAsync(DocumentUploadedCommand command, CancellationToken cancellationToken)
    {
        await RecordEvent(command.DocumentId, ProcessingStage.Uploaded, ProcessingState.Processing);

        try
        {
            var document = await Document.Get(command.DocumentId, cancellationToken);

            await using var contentStream = await _storage.OpenReadAsync(new ObjectReadRequest
            {
                Bucket = command.Bucket,
                ObjectName = command.ObjectKey
            }, cancellationToken);

            // Stage 1: Text Extraction
            if (string.IsNullOrEmpty(document.ExtractedText))
            {
                document.ExtractedText = await ExtractTextAsync(
                    contentStream,
                    document.DisplayName,
                    document.ContentType,
                    cancellationToken);
                await document.Save();
                await RecordEvent(command.DocumentId, ProcessingStage.TextExtracted, ProcessingState.Processing);
            }

            // Stage 2: AI Analysis
            await RecordEvent(command.DocumentId, ProcessingStage.AIAnalysisStarted, ProcessingState.Processing);
            var analysis = await _intelligenceService.AnalyzeDocumentAsync(document, cancellationToken);
            await analysis.Save();
            await RecordEvent(command.DocumentId, ProcessingStage.AIAnalysisCompleted, ProcessingState.Processing);

            // Stage 3: Template Matching
            var templates = await _intelligenceService.FindSimilarTemplates(document, cancellationToken: cancellationToken);
            if (templates.Any())
            {
                document.TemplateId = templates.First().Id;
                await document.Save();
                await RecordEvent(command.DocumentId, ProcessingStage.TemplateMatched, ProcessingState.Processing);
            }

            // Stage 4: Vector Generation (if provider supports it)
            if (Data<Document>.Vector.IsSupported)
            {
                if (document.Embedding == null)
                {
                    document.Embedding = await AI.Embed(document.ExtractedText, cancellationToken: cancellationToken);
                    await document.Save(cancellationToken);
                }
                await RecordEvent(command.DocumentId, ProcessingStage.VectorGenerated, ProcessingState.Processing);
            }

            // Completion
            document.State = ProcessingState.Completed;
            await document.Save(cancellationToken);
            await RecordEvent(command.DocumentId, ProcessingStage.ProcessingCompleted, ProcessingState.Completed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document processing failed for {DocumentId}", command.DocumentId);
            await FlowContext.ScheduleRetryAsync(command, ex, cancellationToken);
            await RecordEvent(command.DocumentId, ProcessingStage.ProcessingFailed, ProcessingState.Failed,
                              new { Error = ex.Message, StackTrace = ex.StackTrace });
        }
    }

    private async Task RecordEvent(Guid documentId, ProcessingStage stage, ProcessingState state, object? data = null)
    {
        var evt = new DocumentProcessingEvent
        {
            DocumentId = documentId,
            Stage = stage,
            State = state,
            EventData = data
        };
        await evt.Save();
    }
}
```

### **4. Auto-Generated APIs with Koan EntityController**

#### **Document API Controller**
```csharp
[Route("api/documents")]
public class DocumentController : EntityController<Document>
{
    private readonly DocumentProcessingOrchestrator _orchestrator;
    private readonly DocumentIntelligenceService _intelligence;
    private readonly IObjectStorageClient _storage;
    private readonly IBackgroundCommandBus _commandBus;

    public DocumentController(
        DocumentProcessingOrchestrator orchestrator,
        DocumentIntelligenceService intelligence,
        IObjectStorageClient storage,
        IBackgroundCommandBus commandBus)
    {
        _orchestrator = orchestrator;
        _intelligence = intelligence;
        _storage = storage;
        _commandBus = commandBus;
    }

    // Auto-generated endpoints from EntityController:
    // GET /api/documents - with pagination, filtering
    // GET /api/documents/{id} - with relationship expansion
    // POST /api/documents - create new
    // PATCH /api/documents/{id} - update
    // DELETE /api/documents/{id} - soft delete

    // Custom endpoints for business logic
    [HttpPost("upload")]
    public async Task<ActionResult<Document>> Upload([FromForm] DocumentUploadRequest request)
    {
        foreach (var file in request.Files)
        {
            await using var sourceStream = file.OpenReadStream();
            await using var hashingStream = new HashingReadStream(sourceStream, SHA512.Create());

            var uploadResult = await _storage.UploadAsync(new ObjectUploadRequest
            {
                Bucket = "documents",
                ObjectName = $"{Guid.NewGuid():N}/{file.FileName}",
                ContentType = file.ContentType,
                Content = hashingStream,
                Metadata = new Dictionary<string, string>
                {
                    ["original-file-name"] = file.FileName,
                    ["content-type"] = file.ContentType
                }
            });

            var sha512 = hashingStream.ComputeHashHex();
            var existing = await Document
                .Where(d => d.Sha512Hash == sha512)
                .FirstOrDefault();

            if (existing != null)
            {
                return Ok(existing); // Deduplication
            }

            var document = new Document
            {
                FileName = uploadResult.ObjectName,
                UserFileName = file.FileName,
                ContentType = file.ContentType,
                FileSize = file.Length,
                Sha512Hash = sha512,
                StorageBucket = uploadResult.Bucket,
                StorageObjectKey = uploadResult.ObjectName,
                StorageVersionId = uploadResult.VersionId
            };

            await document.Save();

            await _commandBus.EnqueueAsync(new DocumentUploadedCommand
            {
                DocumentId = document.Id,
                ObjectKey = uploadResult.ObjectName,
                Bucket = uploadResult.Bucket
            });

            return CreatedAtAction(nameof(GetById), new { id = document.Id }, document);
        }

        return BadRequest();
    }

    [HttpGet("{id}/analysis")]
    public async Task<ActionResult<DocumentAnalysis>> GetAnalysis(Guid id)
    {
        var analysis = await DocumentAnalysis.Where(a => a.DocumentId == id).FirstOrDefault();
        return analysis != null ? Ok(analysis) : NotFound();
    }

    [HttpGet("{id}/similar-templates")]
    public async Task<ActionResult<List<DocumentTemplate>>> GetSimilarTemplates(Guid id)
    {
        var document = await Document.Get(id);
        if (document == null) return NotFound();

        var templates = await _intelligence.FindSimilarTemplates(document);
        return Ok(templates);
    }

    [HttpGet("{id}/processing-history")]
    public async Task<ActionResult<List<DocumentProcessingEvent>>> GetProcessingHistory(Guid id)
    {
        var events = await DocumentProcessingEvent
            .Where(e => e.DocumentId == id)
            .OrderBy(e => e.Timestamp)
            .All();
        return Ok(events);
    }
}
```

#### **Template API Controller**
```csharp
[Route("api/templates")]
public class DocumentTemplateController : EntityController<DocumentTemplate>
{
    private readonly DocumentIntelligenceService _intelligence;

    // Auto-generated CRUD endpoints + custom business logic

    [HttpPost("generate")]
    public async Task<ActionResult<DocumentTemplate>> GenerateTemplate([FromBody] TemplateGenerationRequest request)
    {
        var template = await _intelligence.GenerateTemplateAsync(request.Prompt);
        return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
    }

    [HttpGet("{id}/documents")]
    public async Task<ActionResult<List<Document>>> GetDocuments(Guid id)
    {
        var template = await DocumentTemplate.Get(id);
        if (template == null) return NotFound();

        var documents = await template.GetChildren<Document>(); // Automatic relationship
        return Ok(documents);
    }
}
```

### **5. Multi-Provider Data Strategy**

#### **Provider Configuration**
```csharp
// appsettings.json - Koan auto-detects and elects providers
{
  "Koan": {
    "Data": {
      "Providers": {
        "mongodb": {
          "connectionString": "mongodb://localhost:27017",
          "database": "s13docmind"
        },
        "postgresql": {
          "connectionString": "Host=localhost;Database=s13docmind_audit",
          "priority": 10
        },
        "weaviate": {
          "endpoint": "http://localhost:8080",
          "priority": 5
        },
        "redis": {
          "connectionString": "localhost:6379",
          "priority": 1
        }
      }
    }
  }
}
```

#### **Strategic Provider Assignment**
```csharp
// Provider election happens automatically, but can be influenced:

[DataAdapter("mongodb")] // Document-heavy workloads
public sealed class Document : Entity<Document> { }

[DataAdapter("postgresql")] // ACID-critical audit data
public sealed class DocumentProcessingEvent : FlowEntity<DocumentProcessingEvent> { }

[DataAdapter("weaviate")] // Vector operations
[VectorAdapter("weaviate")]
public sealed class DocumentEmbedding : Entity<DocumentEmbedding> { }

[DataAdapter("redis")] // High-performance caching
public sealed class ProcessingCache : Entity<ProcessingCache> { }
```

### **6. Bootstrap and Auto-Registration**

#### **Program.cs - Minimal Configuration**
```csharp
using DocMind;

var builder = WebApplication.CreateBuilder(args);

// Single line enables all Koan capabilities
builder.Services.AddKoan();

var app = builder.Build();

// Standard ASP.NET Core configuration
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.MapControllers();

app.Run();
```

#### **Koan Auto-Registrar Implementation**
```csharp
// S13.DocMind/KoanAutoRegistrar.cs
public class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // Business services auto-register
        services.AddScoped<DocumentIntelligenceService>();
        services.AddScoped<DocumentProcessingOrchestrator>();
        services.AddScoped<TemplateMatchingService>();

        // AI services auto-configure
        services.Configure<AiOptions>(configuration.GetSection("Koan:AI"));

        // Background services
        services.AddHostedService<DocumentProcessingBackgroundService>();
    }

    public async Task<BootReport> GenerateBootReportAsync(IServiceProvider services)
    {
        var report = new BootReport("S13.DocMind Document Intelligence Platform");

        // Data provider capabilities
        report.AddSection("Data Providers", await GetProviderCapabilities(services));

        // AI capabilities
        report.AddSection("AI Integration", await GetAiCapabilities(services));

        // Processing pipeline status
        report.AddSection("Processing Pipeline", await GetPipelineStatus(services));

        return report;
    }
}
```

---

## **Key Differentiators & Value Proposition**

### **1. Development Velocity**
- **80% Less Boilerplate**: Entity definitions replace repository patterns + manual DI
- **Auto-Generated APIs**: Full CRUD with advanced features (pagination, filtering, relationships)
- **Zero-Configuration AI**: `AI.Prompt()` and `AI.Embed()` replace custom HTTP clients
- **"Reference = Intent"**: Adding package references enables capabilities automatically

### **2. Enterprise Scalability**
- **Multi-Provider Architecture**: Start with MongoDB + Weaviate (core sample), add PostgreSQL/Redis via opt-in packages
- **Provider Transparency**: Same code works across all storage backends
- **Event Sourcing**: Complete audit trail with replay capabilities
- **Container-Native**: Orchestration-aware with automatic environment detection

### **3. AI-Native Capabilities**
- **Built-in Vector Operations**: Semantic search without custom vector pipeline complexity
- **LLM Integration**: Unified interface for multiple AI providers
- **Template Intelligence**: AI-generated templates with similarity matching
- **Multi-Modal Processing**: Text, images, and structured data processing patterns

### **4. Operational Excellence**
- **Capability Discovery**: Auto-generated API documentation with provider capabilities
- **Health Monitoring**: Built-in health checks and performance monitoring
- **Graceful Degradation**: Provider failover with capability-aware fallbacks
- **Event-Driven Architecture**: Real-time processing with streaming capabilities

---

## **Migration Strategy & Implementation Roadmap**

### **Phase 1: Core Entity Migration (Week 1-2)**
- Convert MongoDB models to Koan entities
- Implement `EntityController<T>` for auto-generated APIs
- Set up multi-provider configuration with MongoDB primary

### **Phase 2: AI Integration Enhancement (Week 3-4)**
- Replace custom Ollama client with Koan's AI interface
- Implement vector storage with Weaviate provider
- Add template generation and similarity matching

### **Phase 3: Event Sourcing Implementation (Week 5-6)**
- Implement `FlowEntity` patterns for processing pipeline
- Add event projections for real-time status tracking
- Create processing orchestrator with error handling

### **Phase 4: Advanced Features & Optimization (Week 7-8)**
- Add Redis caching for performance optimization
- Implement streaming responses for real-time updates
- Add comprehensive monitoring and observability

---

## **Conclusion**

**S13.DocMind** demonstrates the transformative power of the Koan Framework, converting a complex document intelligence application from traditional patterns to a modern, AI-native architecture. The solution showcases:

- **Entity-first development** reducing complexity and increasing velocity
- **Multi-provider transparency** enabling seamless scalability
- **Built-in AI capabilities** eliminating infrastructure complexity
- **Event sourcing** providing complete operational visibility
- **Auto-registration** reducing configuration overhead

This architecture serves as a comprehensive reference implementation for building AI-powered document intelligence platforms with enterprise-grade scalability, maintainability, and operational excellence.

---

## **Implementation Requirements & Specifications**

### **1. Technical Prerequisites**

#### **Framework Dependencies**
```xml
<!-- S13.DocMind/S13.DocMind.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core Koan Framework -->
    <PackageReference Include="Koan.Core" Version="1.0.0" />
    <PackageReference Include="Koan.Data" Version="1.0.0" />
    <PackageReference Include="Koan.Web" Version="1.0.0" />
    <PackageReference Include="Koan.AI" Version="1.0.0" />
    <PackageReference Include="Koan.Flow" Version="1.0.0" />

    <!-- Provider Packages - Auto-registered when referenced -->
    <PackageReference Include="Koan.Data.MongoDB" Version="1.0.0" />
    <PackageReference Include="Koan.Data.PostgreSQL" Version="1.0.0" />
    <PackageReference Include="Koan.Data.Weaviate" Version="1.0.0" />
    <PackageReference Include="Koan.Data.Redis" Version="1.0.0" />

    <!-- Document Processing -->
    <PackageReference Include="PdfPig" Version="0.1.9" />
    <PackageReference Include="DocumentFormat.OpenXml" Version="3.0.1" />
    <PackageReference Include="SixLabors.ImageSharp" Version="3.0.2" />
  </ItemGroup>
</Project>
```

#### **Infrastructure Requirements**

**Core sample stack (required to run the walkthrough end-to-end):**

```yaml
services:
  mongodb:
    image: mongo:7.0
    ports: ["27017:27017"]
    environment:
      MONGO_INITDB_DATABASE: s13docmind

  weaviate:
    image: semitechnologies/weaviate:1.22.4
    ports: ["8080:8080"]
    environment:
      QUERY_DEFAULTS_LIMIT: 25
      AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED: 'true'
      PERSISTENCE_DATA_PATH: '/var/lib/weaviate'
      DEFAULT_VECTORIZER_MODULE: 'none'
      ENABLE_MODULES: 'backup-filesystem'
      CLUSTER_HOSTNAME: 'node1'

  ollama:
    image: ollama/ollama:latest
    ports: ["11434:11434"]
    volumes: ["ollama_models:/root/.ollama"]
```

The API container mounts a host directory (for example `./data/storage`) and uses Koan’s filesystem storage provider, so no extra services are required to persist uploaded binaries during demos. This trio keeps the stack lightweight while still highlighting MongoDB, Weaviate, and Ollama working together.

**Optional advanced scenarios:**

- **PostgreSQL** for audit/event sourcing projections and reporting samples.
- **Redis** for distributed caching, rate limiting, and background worker locks.
- **NATS or Azure Service Bus** for durable orchestration (paired with Koan Flow workers).
- **MinIO or cloud object storage** when demonstrating cross-environment replication or bucket lifecycle management.
- **Weaviate replication & GPU inference** to explore high-throughput vector workloads.

Each optional dependency is encapsulated behind Koan adapters so teams can enable them selectively. The sample scripts include compose overrides that wire these extras only when explicitly requested.

### **2. Core Entity Implementation Specifications**

#### **Document Entity - Complete Implementation**
```csharp
namespace S13.DocMind.Models
{
    [DataAdapter("mongodb")] // Primary storage for documents
    [Table("documents")]
    public sealed class Document : Entity<Document>
    {
        // Basic file metadata
        [Required, MaxLength(255)]
        public string FileName { get; set; } = "";

        [MaxLength(255)]
        public string? UserFileName { get; set; }

        [Required, MaxLength(100)]
        public string ContentType { get; set; } = "";

        [Range(1, long.MaxValue)]
        public long FileSize { get; set; }

        // Content storage with size limits
        [MaxLength(10_000_000)] // 10MB text limit
        public string ExtractedText { get; set; } = "";

        // Original content persisted via the configured storage provider (filesystem by default)
        [MaxLength(255)]
        public string? StorageBucket { get; set; }

        [MaxLength(1024)]
        public string? StorageObjectKey { get; set; }

        [MaxLength(100)]
        public string? StorageVersionId { get; set; }

        // SHA-512 hash for deduplication (128 hex chars)
        [Required, Length(128, 128)]
        public string Sha512Hash { get; set; } = "";

        // Processing state tracking
        public ProcessingState State { get; set; } = ProcessingState.Uploaded;
        public DateTime? LastAnalyzed { get; set; }
        public string? ProcessingError { get; set; }

        // User context
        [MaxLength(5000)]
        public string Notes { get; set; } = "";

        // Relationships
        [Parent(typeof(DocumentTemplate))]
        public Guid? TemplateId { get; set; }

        // AI capabilities - when vector provider available
        [Vector(Dimensions = 1536, IndexType = "HNSW")]
        public double[]? Embedding { get; set; }

        // Multi-modal content analysis
        public bool IsImage => ContentType.StartsWith("image/");
        public bool IsTextFile => ContentType == "text/plain";
        public bool IsPdf => ContentType == "application/pdf";
        public bool IsWordDoc => ContentType.Contains("wordprocessingml") || ContentType.Contains("msword");

        // Computed properties
        public string DisplayName => string.IsNullOrWhiteSpace(UserFileName) ? FileName : UserFileName;
        public bool HasEmbedding => Embedding != null && Embedding.Length > 0;
        public bool IsProcessingComplete => State == ProcessingState.Completed;
        public bool HasError => !string.IsNullOrEmpty(ProcessingError);
    }

    public enum ProcessingState
    {
        Uploaded = 0,
        TextExtracting = 1,
        TextExtracted = 2,
        AIAnalyzing = 3,
        AIAnalyzed = 4,
        TemplateMatching = 5,
        EmbeddingGenerated = 6,
        Completed = 7,
        Failed = 8
    }
}
```

#### **Event Sourcing Flow Specification**
```csharp
namespace S13.DocMind.Flows
{
    [DataAdapter("postgresql")] // ACID compliance for audit trail
    [Table("document_processing_events")]
    public sealed class DocumentProcessingEvent : FlowEntity<DocumentProcessingEvent>
    {
        [Required]
        public Guid DocumentId { get; set; }

        [Required]
        public ProcessingStage Stage { get; set; }

        [Required]
        public ProcessingState State { get; set; }

        // Polymorphic event data - JSON serialized
        [Column(TypeName = "jsonb")] // PostgreSQL JSONB for indexing
        public object? EventData { get; set; }

        [MaxLength(2000)]
        public string? ErrorMessage { get; set; }

        public TimeSpan? Duration { get; set; }

        // Processing metrics
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public double? ConfidenceScore { get; set; }

        // Retry tracking
        public int AttemptNumber { get; set; } = 1;
        public string? RetryReason { get; set; }

        // Performance tracking
        public double? CpuUsage { get; set; }
        public long? MemoryUsage { get; set; }
    }
}
```

### **3. AI Integration Specifications**

#### **Document Intelligence Service - Production Ready**
```csharp
namespace S13.DocMind.Services
{
    public interface IDocumentIntelligenceService
    {
        Task<DocumentAnalysis> AnalyzeDocumentAsync(Document document, CancellationToken ct = default);
        Task<List<DocumentTemplate>> FindSimilarTemplatesAsync(Document document, double threshold = 0.8, int limit = 5, CancellationToken ct = default);
        Task<DocumentTemplate> GenerateTemplateAsync(string prompt, CancellationToken ct = default);
        Task<string> ExtractTextAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);
        Task<ProcessingResult> ProcessDocumentWorkflowAsync(Guid documentId, ProcessingOptions? options = null, CancellationToken ct = default);
    }

    public class DocumentIntelligenceService : IDocumentIntelligenceService
    {
        private readonly ILogger<DocumentIntelligenceService> _logger;

        // Koan AI integration - no custom HTTP clients needed
        public async Task<DocumentAnalysis> AnalyzeDocumentAsync(Document document, CancellationToken ct = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Multi-modal analysis based on content type
                var analysisPrompt = document.IsImage
                    ? BuildImageAnalysisPrompt(document)
                    : BuildTextAnalysisPrompt(document.ExtractedText);

                // Koan's unified AI interface with retry and error handling
                var response = await AI.Prompt(analysisPrompt)
                    .WithModel("gpt-4-turbo")
                    .WithMaxTokens(2000)
                    .WithTemperature(0.1)
                    .WithRetry(maxAttempts: 3)
                    .ExecuteAsync(ct);

                // Generate embedding for similarity matching
                var embedding = await AI.Embed(document.ExtractedText)
                    .WithModel("text-embedding-3-large")
                    .ExecuteAsync(ct);

                // Update document with embedding
                document.Embedding = embedding.Vector;
                await document.Save();

                // Parse structured response
                var analysis = ParseAnalysisResponse(response.Content);
                analysis.ProcessingDuration = stopwatch.Elapsed;
                analysis.InputTokens = response.Usage.InputTokens;
                analysis.OutputTokens = response.Usage.OutputTokens;

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze document {DocumentId}", document.Id);
                return new DocumentAnalysis
                {
                    DocumentId = document.Id,
                    Summary = "Analysis failed due to processing error",
                    ConfidenceScore = 0.0,
                    ProcessingError = ex.Message,
                    ProcessingDuration = stopwatch.Elapsed
                };
            }
        }

        // Vector similarity search with automatic provider routing
        public async Task<List<DocumentTemplate>> FindSimilarTemplatesAsync(
            Document document,
            double threshold = 0.8,
            int limit = 5,
            CancellationToken ct = default)
        {
            if (document.Embedding == null)
            {
                _logger.LogWarning("Document {DocumentId} has no embedding for similarity search", document.Id);
                return new List<DocumentTemplate>();
            }

            // Koan's vector operations - automatic provider routing to Weaviate
            var similarTemplates = await DocumentTemplate.Vector
                .SimilaritySearch(document.Embedding)
                .WithThreshold(threshold)
                .WithLimit(limit)
                .ExecuteAsync(ct);

            return similarTemplates;
        }

        private static string BuildTextAnalysisPrompt(string text)
        {
            return $"""
            Analyze the following document and extract structured information:

            DOCUMENT CONTENT:
            {text}

            OUTPUT REQUIREMENTS:
            1. Provide a concise summary (2-3 sentences)
            2. Extract key entities (people, organizations, locations, dates)
            3. Identify main topics and themes
            4. List important facts with confidence scores
            5. Categorize document type

            Response format: JSON with fields: summary, entities, topics, keyFacts, documentType, confidence
            """;
        }
    }

    public class ProcessingOptions
    {
        public bool ForceReprocessing { get; set; } = false;
        public bool GenerateEmbedding { get; set; } = true;
        public bool MatchTemplates { get; set; } = true;
        public string? PreferredModel { get; set; }
        public double ConfidenceThreshold { get; set; } = 0.7;
        public int MaxRetries { get; set; } = 3;
    }
}
```

Operational guidance:

- **Multi-provider routing**: Koan AI profiles map Ollama models for local/offline runs and OpenAI/Azure OpenAI for hosted inference. The sample configuration promotes Ollama by default but automatically fails over when health probes degrade, logging provider swaps.
- **Quality baselines**: A curated benchmark set (10 reference documents + expected JSON outputs) runs nightly via GitHub Actions to detect drift. Failures trigger the human review queue and roll back to the last known-good model profile.
- **Cost guardrails**: `AIUsageBudget` entities track cumulative token spend per environment with alert thresholds; when budgets are exceeded, non-critical template generation calls degrade to smaller models.

### **4. Performance & Scalability Specifications**

#### **Performance Benchmarks**
```csharp
namespace S13.DocMind.Specifications
{
    public static class PerformanceBenchmarks
    {
        // Sample-friendly document processing targets
        public const int MaxDocumentSizeMb = 10;
        public const int MaxTextLengthChars = 1_000_000;
        public const int ConcurrentProcessingLimit = 3;

        // API response time targets (interactive demos)
        public const int DocumentUploadTimeoutMs = 15_000;
        public const int DocumentAnalysisTimeoutMs = 45_000;
        public const int TemplateGenerationTimeoutMs = 30_000;

        // Vector search performance
        public const int VectorSearchTimeoutMs = 2_000;
        public const int MaxVectorSearchResults = 10;
        public const double MinSimilarityThreshold = 0.4;

        // Throughput targets for guided walkthroughs
        public const int DocumentsPerMinute = 4;
        public const int ApiRequestsPerSecond = 5;
        public const int ConcurrentUsers = 3;

        // Stretch goals for optional load experiments
        public const int StretchDocumentsPerMinute = 20;
        public const int StretchConcurrentUsers = 15;
    }
}
```

#### **Caching Strategy**
```csharp
namespace S13.DocMind.Infrastructure
{
    [DataAdapter("redis")]
    public sealed class ProcessingCache : Entity<ProcessingCache>
    {
        [Required]
        public string CacheKey { get; set; } = "";

        public object? CachedData { get; set; }

        public DateTime ExpiresAt { get; set; }

        public CacheType Type { get; set; }

        // TTL management
        public TimeSpan TimeToLive => ExpiresAt - DateTime.UtcNow;
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }

    public enum CacheType
    {
        DocumentAnalysis,
        TemplateMatch,
        VectorSearch,
        ProcessingResult
    }

    public class CachingService
    {
        public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            var cached = await ProcessingCache
                .Where(c => c.CacheKey == key && !c.IsExpired)
                .FirstOrDefault();

            return cached?.CachedData is T data ? data : default(T);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CacheType type = CacheType.DocumentAnalysis)
        {
            var cache = new ProcessingCache
            {
                CacheKey = key,
                CachedData = value,
                ExpiresAt = DateTime.UtcNow.Add(ttl),
                Type = type
            };

            await cache.Save();
        }
    }
}
```

### **5. Security & Compliance Specifications**

#### **Data Security Requirements**
```csharp
namespace S13.DocMind.Security
{
    [DataAdapter("postgresql")]
    [Table("audit_logs")]
    public sealed class AuditLog : Entity<AuditLog>
    {
        [Required]
        public string UserId { get; set; } = "";

        [Required]
        public string Action { get; set; } = "";

        [Required]
        public string ResourceType { get; set; } = "";

        public Guid? ResourceId { get; set; }

        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        [Column(TypeName = "jsonb")]
        public object? AdditionalData { get; set; }

        public AuditResult Result { get; set; } = AuditResult.Success;

        public string? ErrorMessage { get; set; }
    }

    public enum AuditResult
    {
        Success,
        Failed,
        Unauthorized,
        Forbidden
    }

    // Data encryption for sensitive content
    public class EncryptionService
    {
        private const int KeySize = 256;
        private const int IvSize = 128;

        public async Task<byte[]> EncryptAsync(byte[] data, string key)
        {
            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.GenerateIV();

            var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            aes.Key = keyBytes;

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();

            // Prepend IV
            await ms.WriteAsync(aes.IV);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                await cs.WriteAsync(data);
            }

            return ms.ToArray();
        }

        public async Task<byte[]> DecryptAsync(byte[] encryptedData, string key)
        {
            using var aes = Aes.Create();
            aes.KeySize = KeySize;

            var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            aes.Key = keyBytes;

            // Extract IV
            var iv = new byte[IvSize / 8];
            Array.Copy(encryptedData, 0, iv, 0, iv.Length);
            aes.IV = iv;

            var cipherData = new byte[encryptedData.Length - iv.Length];
            Array.Copy(encryptedData, iv.Length, cipherData, 0, cipherData.Length);

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(cipherData);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var result = new MemoryStream();

            await cs.CopyToAsync(result);
            return result.ToArray();
        }
    }
}
```

#### **Sensitive data governance & retention**

- **Automated detection**: Every uploaded object is scanned with Koan's `ContentClassifier` pipeline to tag PII/PHI, contractual clauses, and regulated markers before downstream processing.
- **Selective redaction**: Classified spans are redacted or masked prior to AI prompt submission. The original binary remains sealed in the storage provider (filesystem paths by default, S3-compatible buckets when enabled) with scoped, auditable access policies.
- **Retention policies**: Sample automation applies lightweight file retention (cron job pruning the storage folder after 30 days) and, when an object store is configured, shows how to translate the same policy into bucket lifecycle rules. `RightToBeForgottenFlow` commands scrub embeddings, cached analyses, and audit logs linked to a subject.

#### **Human-in-the-loop validation**

- **Confidence gating**: Analyses with confidence <0.75 or containing high-risk entities are routed to a human review queue (implemented as a Koan Flow state machine) before templates or downstream systems consume them.
- **Exception handling**: Reviewers can approve, request re-run with alternative models, or flag documents for legal escalation. Decisions are persisted on the `DocumentProcessingEvent` stream for full traceability.
- **Operational runbooks**: The sample documentation ships with checklists for weekly access reviews, quarterly audit exports (JSON/CSV), and emergency kill-switch procedures when AI regressions are detected.

### **6. Testing Specifications**

#### **Integration Test Requirements**
```csharp
namespace S13.DocMind.Tests.Integration
{
    [Collection("DatabaseCollection")]
    public class DocumentProcessingWorkflowTests : IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        [Fact]
        public async Task UploadDocument_ShouldTriggerCompleteProcessingWorkflow()
        {
            // Arrange
            var testDocument = CreateTestPdfDocument();
            var uploadRequest = new MultipartFormDataContent();
            uploadRequest.Add(new ByteArrayContent(testDocument.Content), "files", testDocument.FileName);

            // Act - Upload document
            var uploadResponse = await _client.PostAsync("/api/documents/upload", uploadRequest);

            // Assert - Document uploaded successfully
            uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var document = await ParseResponse<Document>(uploadResponse);
            document.FileName.Should().Be(testDocument.FileName);

            // Wait for background processing
            await WaitForProcessingComplete(document.Id, TimeSpan.FromSeconds(30));

            // Assert - Processing completed
            var processingHistory = await GetProcessingHistory(document.Id);
            processingHistory.Should().ContainSingle(e => e.Stage == ProcessingStage.ProcessingCompleted);

            // Assert - Analysis generated
            var analysis = await _client.GetFromJsonAsync<DocumentAnalysis>($"/api/documents/{document.Id}/analysis");
            analysis.Should().NotBeNull();
            analysis.ConfidenceScore.Should().BeGreaterThan(0.5);

            // Assert - Template matching occurred
            var similarTemplates = await _client.GetFromJsonAsync<List<DocumentTemplate>>($"/api/documents/{document.Id}/similar-templates");
            similarTemplates.Should().NotBeNull();
        }

        [Fact]
        public async Task ProcessDocument_WithMediumFile_ShouldHandleGracefully()
        {
            // Test with 8MB file (aligned with sample guidance)
            var largeDocument = CreateLargeTestDocument(8 * 1024 * 1024);
            // ... test implementation
        }

        [Fact]
        public async Task AIAnalysis_WithInvalidContent_ShouldReturnErrorGracefully()
        {
            // Test error handling for corrupted or invalid content
            // ... test implementation
        }

        private async Task WaitForProcessingComplete(Guid documentId, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                var document = await _client.GetFromJsonAsync<Document>($"/api/documents/{documentId}");
                if (document?.State == ProcessingState.Completed || document?.State == ProcessingState.Failed)
                {
                    return;
                }

                await Task.Delay(1000);
            }

            throw new TimeoutException($"Document processing did not complete within {timeout}");
        }
    }

    // Load testing specification
    [Fact]
    public async Task LoadTest_ConcurrentDocumentProcessing_Optional()
    {
        const int concurrentDocuments = 6; // stretch scenario for workshops
        const int maxProcessingTimeMinutes = 3;

        var tasks = Enumerable.Range(0, concurrentDocuments)
            .Select(i => ProcessTestDocument($"test-doc-{i}.txt"))
            .ToArray();

        var completedTasks = await Task.WhenAll(tasks);

        // Assert all documents processed successfully
        completedTasks.Should().AllSatisfy(result =>
            result.State.Should().Be(ProcessingState.Completed));

        // Assert reasonable processing times
        var avgProcessingTime = completedTasks.Average(r => r.ProcessingDuration.TotalSeconds);
        avgProcessingTime.Should().BeLessThan(maxProcessingTimeMinutes * 60);
    }
}
```

**Recommended test progression:**

1. **Smoke walkthrough** – run `DocumentProcessingWorkflowTests.UploadDocument_ShouldTriggerCompleteProcessingWorkflow` with the default sample PDF.
2. **Medium file resilience** – execute `ProcessDocument_WithMediumFile_ShouldHandleGracefully` using the bundled 8 MB fixture.
3. **Optional stretch** – enable the `[Category("Load")]` collection to run `LoadTest_ConcurrentDocumentProcessing_Optional` once infrastructure resources are scaled.

### **7. Deployment & Operations Specifications**

#### **Docker Compose Development Setup (Following S5/S8 Patterns)**

Based on successful patterns from S5.Recs and S8.Flow/S8.Location samples, S13.DocMind provides multiple deployment scenarios:

##### **Option 1: API with Embedded Client (S5.Recs Pattern)**
```yaml
# docker-compose.yml - Simple embedded client in API wwwroot
version: '3.8'
services:
  mongodb:
    image: mongo:7
    container_name: s13-docmind-mongo
    healthcheck:
      test: ["CMD", "mongosh", "--eval", "db.adminCommand('ping')"]
      interval: 5s
      timeout: 5s
      retries: 10
    ports:
      - "4920:27017"
    volumes:
      - mongo_data:/data/db
    environment:
      MONGO_INITDB_DATABASE: s13docmind

  postgresql:
    image: postgres:15
    container_name: s13-docmind-postgres
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U docmind"]
      interval: 5s
      timeout: 5s
      retries: 10
    ports:
      - "4921:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    environment:
      POSTGRES_DB: s13docmind_audit
      POSTGRES_USER: docmind
      POSTGRES_PASSWORD: docmind123

  weaviate:
    image: semitechnologies/weaviate:1.22.4
    container_name: s13-docmind-weaviate
    ports:
      - "4922:8080"
    environment:
      QUERY_DEFAULTS_LIMIT: 25
      AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED: 'true'
      PERSISTENCE_DATA_PATH: '/var/lib/weaviate'
      DEFAULT_VECTORIZER_MODULE: 'none'
      ENABLE_MODULES: 'backup-filesystem'
      CLUSTER_HOSTNAME: 'node1'
    volumes:
      - weaviate_data:/var/lib/weaviate
    healthcheck:
      test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8080/v1/.well-known/ready"]
      interval: 5s
      timeout: 5s
      retries: 10

  redis:
    image: redis:7-alpine
    container_name: s13-docmind-redis
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 5s
      retries: 10
    ports:
      - "4923:6379"
    volumes:
      - redis_data:/data
    command: redis-server --appendonly yes

  # Ollama for local AI processing
  ollama:
    image: ollama/ollama:latest
    container_name: s13-docmind-ollama
    ports:
      - "4924:11434"
    volumes:
      - ollama_models:/root/.ollama
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:11434/api/version"]
      interval: 30s
      timeout: 10s
      retries: 5
    environment:
      - OLLAMA_MODELS_DIR=/root/.ollama

  # Main API with embedded web client in wwwroot (S5.Recs pattern)
  docmind-api:
    build:
      context: ../../..  # Build from repo root like S8 samples
      dockerfile: samples/S13.DocMind/Dockerfile
    container_name: s13-docmind-api
    environment:
      ASPNETCORE_URLS: http://+:4925
      ASPNETCORE_ENVIRONMENT: Development
      # Koan provider configuration (S8 pattern)
      Koan__Data__Providers__mongodb__connectionString: mongodb://mongodb:27017
      Koan__Data__Providers__mongodb__database: s13docmind
      Koan__Data__Providers__postgresql__connectionString: Host=postgresql;Database=s13docmind_audit;Username=docmind;Password=docmind123
      Koan__Data__Providers__weaviate__endpoint: http://weaviate:8080
      Koan__Data__Providers__redis__connectionString: redis:6379
      # AI Configuration
      Koan__AI__Ollama__BaseUrl: http://ollama:11434
      Koan__AI__OpenAI__ApiKey: ${OPENAI_API_KEY:-}
      # Document processing limits
      S13__DocMind__MaxDocumentSizeMB: 50
      S13__DocMind__ConcurrentProcessingLimit: 10
    depends_on:
      mongodb:
        condition: service_healthy
      postgresql:
        condition: service_healthy
      weaviate:
        condition: service_healthy
      redis:
        condition: service_healthy
      ollama:
        condition: service_healthy
    ports:
      - "4925:4925"
    volumes:
      - document_storage:/app/storage  # For large document files
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:4925/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s

volumes:
  mongo_data:
  postgres_data:
  weaviate_data:
  redis_data:
  ollama_models:
  document_storage:
```

##### **Option 2: Separate Client Container (S8.Location Pattern)**
```yaml
# docker-compose.separate-client.yml - Client as separate nginx container
version: '3.8'
services:
  # ... same infrastructure services as above ...

  # API without embedded client
  docmind-api:
    build:
      context: ../../..
      dockerfile: samples/S13.DocMind/S13.DocMind.Api/Dockerfile
    container_name: s13-docmind-api
    environment:
      ASPNETCORE_URLS: http://+:4926
      # ... same environment variables as Option 1 ...
    depends_on:
      mongodb:
        condition: service_healthy
      postgresql:
        condition: service_healthy
      weaviate:
        condition: service_healthy
      redis:
        condition: service_healthy
    ports:
      - "4926:4926"

  # Separate React/Vue client container (S8.Location pattern)
  docmind-client:
    build:
      context: ../S13.DocMind.Client
      dockerfile: Dockerfile
    container_name: s13-docmind-client
    ports:
      - "4927:80"  # Client on port 4927
    depends_on:
      - docmind-api
    healthcheck:
      test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:80/"]
      interval: 30s
      timeout: 10s
      retries: 3
    # nginx configuration for API proxying built into Dockerfile
```

#### **Dockerfile Configurations**

##### **API Dockerfile (Based on S8 Pattern)**
```dockerfile
# samples/S13.DocMind/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .

# Restore and build from repo root context (S8 pattern)
RUN dotnet restore samples/S13.DocMind/S13.DocMind.csproj
RUN dotnet publish samples/S13.DocMind/S13.DocMind.csproj -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Create storage directory for document files
RUN mkdir -p /app/storage && chmod 755 /app/storage

# Expose port
EXPOSE 4925

ENTRYPOINT ["dotnet", "S13.DocMind.dll"]
```

##### **Separate Client Dockerfile (S8.Location Pattern)**
```dockerfile
# samples/S13.DocMind.Client/Dockerfile
FROM node:20-alpine AS build
WORKDIR /app

# Copy package files
COPY package*.json ./
RUN npm ci

# Copy source and build
COPY . .
RUN npm run build

# Production stage with nginx
FROM nginx:alpine

# Install wget for health checks
RUN apk add --no-cache wget

# Copy built client files
COPY --from=build /app/dist /usr/share/nginx/html

# Create nginx configuration with API proxy
RUN echo 'server { \
    listen 80; \
    server_name localhost; \
    root /usr/share/nginx/html; \
    index index.html; \
    \
    # Client-side routing \
    location / { \
        try_files $uri $uri/ /index.html; \
    } \
    \
    # API proxy to backend \
    location /api/ { \
        proxy_pass http://docmind-api:4926/api/; \
        proxy_http_version 1.1; \
        proxy_set_header Upgrade $http_upgrade; \
        proxy_set_header Connection "upgrade"; \
        proxy_set_header Host $host; \
        proxy_set_header X-Real-IP $remote_addr; \
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for; \
        proxy_set_header X-Forwarded-Proto $scheme; \
        proxy_read_timeout 300s; \
        proxy_connect_timeout 75s; \
        client_max_body_size 50m; \
    } \
    \
    # Health check endpoint \
    location /health { \
        proxy_pass http://docmind-api:4926/health; \
        proxy_http_version 1.1; \
        proxy_set_header Host $host; \
    } \
    \
    # WebSocket support for real-time updates \
    location /ws { \
        proxy_pass http://docmind-api:4926/ws; \
        proxy_http_version 1.1; \
        proxy_set_header Upgrade $http_upgrade; \
        proxy_set_header Connection "upgrade"; \
        proxy_set_header Host $host; \
    } \
}' > /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

#### **API Static File Configuration (S5.Recs Pattern)**

For embedded client hosting in API wwwroot, the Koan framework auto-wires static files:

##### **Program.cs Configuration**
```csharp
using S13.DocMind;

var builder = WebApplication.CreateBuilder(args);

// Single line enables Koan with auto-static file serving (S5 pattern)
builder.Services.AddKoan()
    .AsWebApi()           // Enables API controllers
    .AsProxiedApi()       // Enables reverse proxy support
    .WithRateLimit();     // Adds rate limiting

var app = builder.Build();

// Koan.Web startup filter auto-wires:
// - Static files from wwwroot
// - Controller routing
// - Swagger endpoints
// - Health checks

// Custom middleware for document processing
app.UseDocumentProcessing();  // Custom middleware for file uploads

app.Run();

namespace S13.DocMind
{
    public partial class Program { }
}
```

##### **Client Files in wwwroot Structure**
```
samples/S13.DocMind/wwwroot/
├── index.html              # Main SPA entry point
├── js/
│   ├── app.js             # Main application logic
│   ├── document-upload.js # Document upload handling
│   ├── template-editor.js # Template editing
│   └── analysis-viewer.js # Analysis result viewer
├── css/
│   ├── styles.css         # Main stylesheet
│   └── components.css     # Component styles
├── images/
│   ├── logo.png
│   └── icons/
└── lib/                   # Third-party libraries
    ├── axios.min.js
    ├── marked.min.js      # Markdown rendering
    └── highlight.min.js   # Code syntax highlighting
```

##### **Client-Side API Integration**
```javascript
// wwwroot/js/app.js - API integration with auto-discovery
class DocMindApi {
    constructor() {
        // Auto-detect API base URL (works in both embedded and proxied scenarios)
        this.baseUrl = window.location.origin;
        this.apiPath = '/api';
    }

    async uploadDocuments(files) {
        const formData = new FormData();
        files.forEach(file => formData.append('files', file));

        const response = await fetch(`${this.baseUrl}${this.apiPath}/documents/upload`, {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            throw new Error(`Upload failed: ${response.statusText}`);
        }

        return response.json();
    }

    async getDocumentAnalysis(documentId) {
        const response = await fetch(`${this.baseUrl}${this.apiPath}/documents/${documentId}/analysis`);
        return response.json();
    }

    async generateTemplate(prompt) {
        const response = await fetch(`${this.baseUrl}${this.apiPath}/templates/generate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ prompt })
        });
        return response.json();
    }

    // WebSocket for real-time processing updates
    connectToProcessingUpdates(documentId, callback) {
        const ws = new WebSocket(`ws://${window.location.host}/ws/documents/${documentId}/processing`);
        ws.onmessage = (event) => callback(JSON.parse(event.data));
        return ws;
    }
}
```

#### **Environment-Specific Configurations**

##### **Development (docker-compose.yml)**
```yaml
# Optimized for development with local Ollama
environment:
  ASPNETCORE_ENVIRONMENT: Development
  # Use local Ollama instance
  Koan__AI__Ollama__BaseUrl: http://host.docker.internal:11434
  # Enable detailed logging
  Logging__LogLevel__S13.DocMind: Debug
  Logging__LogLevel__Koan.AI: Debug
  # Relaxed file upload limits for testing
  S13__DocMind__MaxDocumentSizeMB: 100
  S13__DocMind__AllowTestDocuments: "true"
```

##### **Production (docker-compose.production.yml)**
```yaml
# Production with external AI services
environment:
  ASPNETCORE_ENVIRONMENT: Production
  # Use OpenAI for production AI
  Koan__AI__OpenAI__ApiKey: ${OPENAI_API_KEY}
  Koan__AI__OpenAI__Model: gpt-4-turbo
  # Strict security settings
  S13__DocMind__MaxDocumentSizeMB: 50
  S13__DocMind__AllowTestDocuments: "false"
  S13__DocMind__RequireAuthentication: "true"
  # Production database with replication
  Koan__Data__Providers__mongodb__connectionString: mongodb://mongo-primary:27017,mongo-secondary:27017/s13docmind?replicaSet=rs0
```

#### **Quick Start Scripts (S8 Pattern)**

##### **start.sh - Development Startup**
```bash
#!/bin/bash
# samples/S13.DocMind/start.sh

echo "🚀 Starting S13.DocMind Development Environment..."

# Check prerequisites
command -v docker >/dev/null 2>&1 || { echo "Docker is required"; exit 1; }
command -v docker-compose >/dev/null 2>&1 || { echo "Docker Compose is required"; exit 1; }

# Start with embedded client (default)
echo "📦 Starting with embedded client in API wwwroot..."
docker-compose -f docker-compose.yml up --build -d

# Wait for services to be healthy
echo "⏳ Waiting for services to be ready..."
timeout 120 bash -c '
  while ! docker-compose ps | grep -q "healthy"; do
    echo "  Waiting for health checks..."
    sleep 5
  done
'

echo "✅ S13.DocMind is ready!"
echo "🌐 Web Interface: http://localhost:4925"
echo "📚 API Documentation: http://localhost:4925/swagger"
echo "🔍 Health Check: http://localhost:4925/health"

# Optional: Start with separate client
if [ "$1" = "--separate-client" ]; then
    echo "🔄 Starting with separate client container..."
    docker-compose -f docker-compose.separate-client.yml up --build -d
    echo "🌐 API: http://localhost:4926"
    echo "🌐 Client: http://localhost:4927"
fi
```

##### **stop.sh - Cleanup Script**
```bash
#!/bin/bash
# samples/S13.DocMind/stop.sh

echo "🛑 Stopping S13.DocMind..."

docker-compose -f docker-compose.yml down
docker-compose -f docker-compose.separate-client.yml down 2>/dev/null || true

if [ "$1" = "--clean" ]; then
    echo "🧹 Cleaning up volumes and images..."
    docker-compose -f docker-compose.yml down -v --rmi local
    docker system prune -f
fi

echo "✅ S13.DocMind stopped"
```

#### **Container Orchestration (Production)**

The primary sample compose file (`docker-compose.yml`) boots the minimal stack—MongoDB, Weaviate, and Ollama—alongside the API container that mounts a local storage folder. The production variant below illustrates how to layer on optional dependencies (PostgreSQL for auditing, Redis for caching, externalized OpenAI access, object storage services, etc.) when demonstrating advanced scenarios.
```yaml
# docker-compose.production.yml
version: '3.8'
services:
  docmind-api:
    build:
      context: .
      dockerfile: Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Koan__Data__Providers__mongodb__connectionString=mongodb://mongo-primary:27017,mongo-secondary:27017/s13docmind?replicaSet=rs0
      - Koan__Data__Providers__postgresql__connectionString=Host=postgres-primary;Database=s13docmind_audit;Username=docmind;Password=${POSTGRES_PASSWORD}
      - Koan__Data__Providers__weaviate__endpoint=http://weaviate:8080
      - Koan__Data__Providers__redis__connectionString=redis-cluster:6379
      - Koan__AI__OpenAI__ApiKey=${OPENAI_API_KEY}
      - Koan__AI__Ollama__BaseUrl=http://ollama:11434
    ports: ["8080:8080"]
    depends_on:
      - mongo-primary
      - postgres-primary
      - weaviate
      - redis-cluster
      - ollama
    deploy:
      replicas: 3
      resources:
        limits: {cpus: '2.0', memory: 4G}
        reservations: {cpus: '1.0', memory: 2G}
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s

  # MongoDB replica set for production
  mongo-primary:
    image: mongo:7.0
    command: mongod --replSet rs0 --bind_ip_all
    volumes: ["mongo-primary-data:/data/db"]

  mongo-secondary:
    image: mongo:7.0
    command: mongod --replSet rs0 --bind_ip_all
    volumes: ["mongo-secondary-data:/data/db"]

  # PostgreSQL with replication
  postgres-primary:
    image: postgres:15
    environment:
      POSTGRES_DB: s13docmind_audit
      POSTGRES_USER: docmind
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_REPLICATION_USER: replicator
      POSTGRES_REPLICATION_PASSWORD: ${REPLICATION_PASSWORD}
    volumes: ["postgres-primary-data:/var/lib/postgresql/data"]

  # Weaviate cluster
  weaviate:
    image: semitechnologies/weaviate:1.22.4
    environment:
      QUERY_DEFAULTS_LIMIT: 25
      AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED: 'false'
      AUTHENTICATION_OIDC_ENABLED: 'true'
      PERSISTENCE_DATA_PATH: '/var/lib/weaviate'
      DEFAULT_VECTORIZER_MODULE: 'none'
      ENABLE_MODULES: 'backup-filesystem,offload-s3'
      CLUSTER_HOSTNAME: 'node1'
    volumes: ["weaviate-data:/var/lib/weaviate"]

  # Redis cluster
  redis-cluster:
    image: redis:7-alpine
    command: redis-server --appendonly yes --cluster-enabled yes
    volumes: ["redis-data:/data"]

  # Ollama for local AI
  ollama:
    image: ollama/ollama:latest
    volumes: ["ollama-models:/root/.ollama"]
    environment:
      - OLLAMA_MODELS_DIR=/root/.ollama
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu]

volumes:
  mongo-primary-data:
  mongo-secondary-data:
  postgres-primary-data:
  weaviate-data:
  redis-data:
  ollama-models:
```

#### **Health Monitoring Specification**
```csharp
namespace S13.DocMind.Health
{
    public class DocMindHealthCheck : IHealthCheck
    {
        private readonly KoanOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IObjectStorageClient _storage;

        public DocMindHealthCheck(
            IOptions<KoanOptions> options,
            IHttpClientFactory httpClientFactory,
            IObjectStorageClient storage)
        {
            _options = options.Value;
            _httpClientFactory = httpClientFactory;
            _storage = storage;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            var checks = new List<(string name, Func<Task<bool>> check)>
            {
                ("MongoDB", CheckMongoHealthAsync),
                ("Weaviate", CheckWeaviateHealthAsync),
                ("Ollama", CheckOllamaHealthAsync),
                ("Storage Provider", CheckObjectStorageHealthAsync),
                ("Document Processing", CheckProcessingHealthAsync)
            };

            if (IsProviderEnabled("postgresql"))
            {
                checks.Add(("PostgreSQL", CheckPostgresHealthAsync));
            }

            if (IsProviderEnabled("redis"))
            {
                checks.Add(("Redis", CheckRedisHealthAsync));
            }

            if (IsAiProviderEnabled("openai"))
            {
                checks.Add(("OpenAI", CheckOpenAiHealthAsync));
            }

            var results = await Task.WhenAll(checks.Select(async c => new
            {
                c.name,
                result = await c.check()
            }));
            var failures = results.Where(r => !r.result).ToList();

            if (failures.Any())
            {
                var failureNames = string.Join(", ", failures.Select(f => f.name));
                return HealthCheckResult.Unhealthy($"Failed components: {failureNames}");
            }

            return HealthCheckResult.Healthy("All systems operational");
        }

        private async Task<bool> CheckMongoHealthAsync()
        {
            try
            {
                _ = await Document.Take(1);
                return true;
            }
            catch { return false; }
        }

        private async Task<bool> CheckAiHealthAsync()
        {
            try
            {
                var response = await AI.Prompt("Test")
                    .WithTimeout(TimeSpan.FromSeconds(10))
                    .ExecuteAsync();
                return !string.IsNullOrEmpty(response.Content);
            }
            catch { return false; }
        }

        private async Task<bool> CheckOllamaHealthAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient("ollama-health");
                var response = await client.GetAsync("/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private async Task<bool> CheckObjectStorageHealthAsync()
        {
            try
            {
                await _storage.EnsureBucketExistsAsync("documents");
                return true;
            }
            catch { return false; }
        }

        private async Task<bool> CheckOpenAiHealthAsync()
        {
            try
            {
                var response = await AI.Prompt("ping")
                    .WithProvider("openai")
                    .WithTimeout(TimeSpan.FromSeconds(5))
                    .ExecuteAsync();
                return !string.IsNullOrEmpty(response.Content);
            }
            catch { return false; }
        }

        private bool IsProviderEnabled(string providerKey)
            => _options.Data?.Providers?.ContainsKey(providerKey) == true;

        private bool IsAiProviderEnabled(string providerKey)
            => _options.AI?.ContainsKey(providerKey) == true;
    }
}
```

### **8. Success Criteria & Acceptance Testing**

#### **Functional Requirements Checklist**
- [ ] **Document Upload**: Support .txt, .pdf, .docx, and image formats up to 10 MB each (stretch: 25 MB with streaming enabled).
- [ ] **Text Extraction**: Demonstrate ≥95 % accuracy on the curated sample pack; document gaps for edge formats.
- [ ] **AI Analysis**: Produce structured summaries with confidence scoring and human-review routing.
- [ ] **Template System**: Generate templates via AI and persist review decisions.
- [ ] **Vector Search**: Return top-5 similar templates in <2 s across the 200-document demo corpus.
- [ ] **Event Sourcing**: Persist a complete audit trail of upload → analysis Flow events.
- [ ] **Multi-Provider (core)**: Operate across MongoDB, Weaviate, and Ollama; document toggles for optional Redis/PostgreSQL.
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
| `GDoc.Api.Models.UploadedDocument` | `S13.DocMind.Models.Document` | Convert to Entity<T>, add vector support | Property definitions, validation logic |
| `GDoc.Api.Models.DocumentationRequest` | `S13.DocMind.Models.DocumentAnalysis` | Restructure as analysis result entity | Request processing logic |
| `GDoc.Api.Models.DocumentTypeConfiguration` | `S13.DocMind.Models.DocumentTemplate` | Convert to Entity<T>, add AI generation | Template structure, validation rules |
| `GDoc.Api.Services.DocumentProcessor` | `S13.DocMind.Services.DocumentIntelligenceService` | Replace custom clients with AI.Prompt() | Text extraction methods |
| `GDoc.Api.Services.LlmService` | Built-in Koan AI interface | Remove custom HTTP client code | Prompt building logic |
| `GDoc.Api.Repositories.*Repository` | Automatic via Entity<T> patterns | Remove repository classes | Query logic for custom endpoints |
| `GDoc.Api.Controllers.*Controller` | `EntityController<T>` inheritance | Replace manual CRUD with inheritance | Business logic endpoints |
| `Program.cs` DI registration | `KoanAutoRegistrar` | Move to auto-registrar pattern | Service configuration logic |

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
// Target: KoanAutoRegistrar pattern

// MIGRATE FROM:
// builder.Services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();
// builder.Services.AddScoped<UploadedDocumentRepository>();
// builder.Services.AddSingleton<LlmService>();
// ... (58 more lines)

// MIGRATE TO:
public class KoanAutoRegistrar : IKoanAutoRegistrar
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
      postgresql:
        connectionString: "Host=postgres;Database=s13docmind_audit;Username=docmind;Password=${POSTGRES_PASSWORD}"
        priority: 10
      weaviate:
        endpoint: "http://weaviate:8080"
        priority: 7
      redis:
        connectionString: "redis:6379"
        priority: 1
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
- [ ] **Manual DI registration** (convert to auto-registration)
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
| **Phase 5: Infrastructure** | Program.cs, configs | KoanAutoRegistrar | Migrate service registrations |

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
4. Consider enabling Redis caching (optional component)
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