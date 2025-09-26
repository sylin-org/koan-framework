## **S13.DocMind Refactoring Blueprint – Data & Domain Model**

### 1. Domain Framing
S13.DocMind evolves from a file-centric demo into a document intelligence platform. The refactor organizes the solution around four cohesive domains:

| Domain | Responsibility | Koan Capabilities | Primary Models |
|--------|----------------|-------------------|----------------|
| **Intake** | Accept uploads, deduplicate binaries, project status | `EntityController<T>`, storage abstractions | `SourceDocument`, `DocumentUploadReceipt` |
| **Templates & Types** | Define semantic document profiles, manage prompts | Koan AI prompt catalogs, MCP entities | `SemanticTypeProfile`, `TemplateSection` |
| **Processing** | Extract content, synthesize insights, maintain history | Hosted background services, Koan AI, Mongo data adapters | `DocumentChunk`, `DocumentInsight`, `DocumentProcessingEvent` |
| **Discovery** | Surface search, analytics, and MCP tooling | Vector adapters, Koan search filters, MCP resources | `InsightCollection`, `SimilarityProjection` |

The existing Docker Compose stack (API + MongoDB + Weaviate + Ollama) remains the canonical deployment baseline. Each domain uses MongoDB as the system of record and optional Weaviate embeddings when vector search is enabled.

### 2. Core Entities with Intentful Naming
The refactor introduces semantically rich models that align terminology across the API, documentation, and UI. All entities rely on automatic data adapter and table mapping resolution by the Koan Framework - no explicit `[DataAdapter]` or `[Table]` attributes are needed.

#### `SourceDocument`
Stores immutable upload metadata plus the latest processing summary.

```csharp
[McpEntity(Name = "source-documents", Description = "Uploaded documents pending or completing AI analysis")]
public sealed class SourceDocument : Entity<SourceDocument>
{
    [Required, MaxLength(255)] public string FileName { get; set; } = string.Empty;
    [MaxLength(255)] public string? DisplayName { get; set; }
    [Required, MaxLength(120)] public string ContentType { get; set; } = string.Empty;
    [Range(1, long.MaxValue)] public long FileSizeBytes { get; set; }
    [Required, Length(128,128)] public string Sha512 { get; set; } = string.Empty;

    [MaxLength(120)] public string StorageBucket { get; set; } = "local";
    [MaxLength(512)] public string StorageObjectKey { get; set; } = string.Empty;

    public DocumentProcessingStatus Status { get; set; } = DocumentProcessingStatus.Uploaded;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastProcessedAt { get; set; }
    public string? LastError { get; set; }

    // Relationships
    [Parent(typeof(SemanticTypeProfile))] public Guid? AssignedProfileId { get; set; }
    public bool AssignedBySystem { get; set; }

    // Quick access projections
    public DocumentProcessingSummary Summary { get; set; } = new();
}
```

#### `DocumentProcessingSummary`
Value object embedded in `SourceDocument` so the UI and MCP clients can render analysis without joining other collections.

```csharp
public sealed class DocumentProcessingSummary
{
    public bool TextExtracted { get; set; }
    public bool VisionExtracted { get; set; }
    public double? AutoClassificationConfidence { get; set; }
    public string? PrimaryFindings { get; set; }
    public IReadOnlyList<InsightReference> InsightRefs { get; set; } = Array.Empty<InsightReference>();
    public IReadOnlyList<ChunkReference> ChunkRefs { get; set; } = Array.Empty<ChunkReference>();
}
```

#### `SemanticTypeProfile`
Represents the intent of a document type, including the canonical prompt template consumed by Koan AI and MCP tools.

```csharp
[McpEntity(Name = "semantic-type-profiles", Description = "Document templates with AI instructions")]
public sealed class SemanticTypeProfile : Entity<SemanticTypeProfile>
{
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(60)] public string Code { get; set; } = string.Empty;
    [MaxLength(1000)] public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();

    public PromptTemplate Prompt { get; set; } = new();
    public TemplateExtractionSchema ExtractionSchema { get; set; } = new();
    public List<string> ExamplePhrases { get; set; } = new();
}
```

#### `DocumentChunk`
Captures chunked text blocks produced during extraction. Each chunk stores the derived insights to avoid repeated joins during aggregation.

```csharp
[Parent(typeof(SourceDocument))]
public sealed class DocumentChunk : Entity<DocumentChunk>
{
    public Guid SourceDocumentId { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public bool IsLastChunk { get; set; }

    public List<InsightReference> InsightRefs { get; set; } = new();
}
```

#### `DocumentInsight`
Stores structured outcomes from AI runs. Insights are additive so multiple pipelines (text, vision, aggregation) can contribute facts over time.

```csharp
[Parent(typeof(SourceDocument))]
public sealed class DocumentInsight : Entity<DocumentInsight>
{
    public Guid SourceDocumentId { get; set; }
    public InsightChannel Channel { get; set; }
    public string? Section { get; set; }
    public string Heading { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public IReadOnlyDictionary<string, object?> StructuredPayload { get; set; } = new Dictionary<string, object?>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

#### `DocumentProcessingEvent`
Supports diagnostics, retries, and front-end status toasts without exposing infrastructure internals.

```csharp
[Parent(typeof(SourceDocument))]
public sealed class DocumentProcessingEvent : Entity<DocumentProcessingEvent>
{
    public Guid SourceDocumentId { get; set; }
    public ProcessingStage Stage { get; set; }
    public DocumentProcessingStatus Status { get; set; }
    public string? Detail { get; set; }
    public string? Error { get; set; }
    public TimeSpan? Duration { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
}
```

### 3. Vector Data Entities (Optional Weaviate Integration)

#### `SemanticTypeEmbedding`
Stores vector embeddings for semantic type profiles when Weaviate is enabled.

```csharp
[VectorAdapter("weaviate")]
public sealed class SemanticTypeEmbedding : Entity<SemanticTypeEmbedding>
{
    public Guid SemanticTypeProfileId { get; set; }
    [Vector(Dimensions = 1536, IndexType = "HNSW")] public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

#### `DocumentChunkEmbedding`
Stores vector embeddings for document chunks when vector search is enabled.

```csharp
[VectorAdapter("weaviate")]
public sealed class DocumentChunkEmbedding : Entity<DocumentChunkEmbedding>
{
    public Guid DocumentChunkId { get; set; }
    public Guid SourceDocumentId { get; set; }
    [Vector(Dimensions = 1536, IndexType = "HNSW")] public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

### 4. Supporting Enumerations & Value Objects
- `DocumentProcessingStatus`: `Uploaded`, `Queued`, `Extracting`, `Extracted`, `Analyzing`, `InsightsReady`, `Completed`, `Failed`.
- `ProcessingStage`: `Upload`, `Deduplicate`, `ExtractText`, `ExtractVision`, `GenerateInsights`, `GenerateEmbedding`, `Aggregate`.
- `InsightChannel`: `Text`, `Vision`, `Aggregation`, `UserFeedback`.
- Value objects: `InsightReference`, `ChunkReference`, `PromptTemplate`, `TemplateExtractionSchema`, `DocumentUploadReceipt`.

### 5. Refactoring Plan (Data Layer)
1. **Establish shared abstractions**: Introduce value objects and enums in a `S13.DocMind.Domain` project; update controllers and services to reference these instead of ad-hoc strings.
2. **Migrate storage schema**: Create Mongo migrations (or bootstrap tasks) to transform existing `files` documents into `source_documents` with embedded summaries while leaving raw binary paths untouched.
3. **Adopt clear naming**: Rename DbSets, repositories, and Angular models to match the new entity names and regenerate TypeScript clients with `dotnet koan client` for schema alignment.
4. **Centralize chunk + insight creation**: Move chunking logic into a dedicated `ChunkProjectionService` invoked by the background pipeline so controllers no longer build chunks inline.
5. **Vector enablement through separate entities**: Use `[VectorAdapter("weaviate")]` on dedicated embedding entities (`SemanticTypeEmbedding`, `DocumentChunkEmbedding`) so core entities remain provider-agnostic. Core entities use automatic adapter resolution. This allows the sample to run without Weaviate while enabling vector search when available.
6. **Telemetry-first data updates**: Persist `DocumentProcessingEvent` entries whenever the hosted worker transitions stages; expose a query endpoint for troubleshooting and UI timelines.

### 6. Opportunities to Simplify Developer Experience
- **Schema-driven UI scaffolding**: Generate TypeScript models directly from Koan metadata so Angular forms automatically surface prompt sections, tags, and structured extraction fields.
- **Portable storage abstraction**: Keep binary storage in Koan’s filesystem provider but expose a single configuration key (`StorageProvider:Kind`) for teams to switch to S3/Blob storage using Koan’s adapters without code changes.
- **Shared validation policies**: Reuse Koan `IValidationRule<T>` implementations for file size, MIME type, and prompt completeness to maintain parity between API and MCP workflows.
- **Analysis snapshots**: Persist `InsightCollection` aggregates that flatten the latest insights per profile so dashboards and MCP clients can issue a single query for “document at a glance.”

This data blueprint establishes a clear, intention-revealing foundation that subsequent chunks build upon when detailing AI pipelines, API shape, and UI behavior.
