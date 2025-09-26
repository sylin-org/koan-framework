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
    <PackageReference Include="Koan.Data.Weaviate" Version="1.0.0" />

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
    // Processing events stored in MongoDB with other entities
    public sealed class DocumentProcessingEvent : FlowEntity<DocumentProcessingEvent>
    {
        [Required]
        public Guid DocumentId { get; set; }

        [Required]
        public ProcessingStage Stage { get; set; }

        [Required]
        public ProcessingState State { get; set; }

        // Polymorphic event data - JSON serialized
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

### **3.5. Enhanced Services Implementation (GDoc Feature Parity)**

#### **Rich Document Analysis Service**
```csharp
namespace S13.DocMind.Services.Enhanced
{
    public class EnhancedDocumentAnalysisService
    {
        private readonly ILogger<EnhancedDocumentAnalysisService> _logger;
        private readonly DocumentTypeMatchingService _typeMatching;

        public async Task<ExtractedDocumentInformation> AnalyzeDocumentStructuredAsync(
            Document document, DocumentType type, CancellationToken ct = default)
        {
            var extractionPrompt = $"""
                COMPREHENSIVE DOCUMENT ANALYSIS

                Document Type Context: {type.ExtractionPrompt}
                Content: {document.ExtractedText}

                Extract and structure:
                1. ENTITIES: People, organizations, dates, locations, technical terms
                2. TOPICS: Main themes and subjects
                3. KEY_FACTS: Decisions, action items, requirements with confidence
                4. STRUCTURED_DATA: Dates, numbers, structured information
                5. SUMMARY: Purpose and content overview

                JSON Response Format:
                {{
                    "entities": {{ "people": [...], "organizations": [...], "dates": [...] }},
                    "topics": [...],
                    "inferredDocumentType": "category",
                    "keyFacts": [{{ "type": "decision", "content": "...", "confidence": 0.9, "context": "..." }}],
                    "structuredData": {{ "key": "value" }},
                    "summary": "Document summary",
                    "confidenceScore": 0.85
                }}
                """;

            var response = await AI.Prompt(extractionPrompt)
                .WithModel("gpt-4-turbo")
                .WithMaxTokens(3000)
                .WithTemperature(0.1)
                .ExecuteAsync(ct);

            return JsonSerializer.Deserialize<ExtractedDocumentInformation>(response.Content)
                ?? CreateFallbackExtraction(response.Content);
        }

        private ExtractedDocumentInformation CreateFallbackExtraction(string content)
        {
            return new ExtractedDocumentInformation
            {
                Summary = content.Length > 500 ? content[..500] + "..." : content,
                InferredDocumentType = "unknown",
                ConfidenceScore = 0.5,
                ExtractedAt = DateTime.UtcNow
            };
        }
    }
}
```

#### **Image Understanding Service Implementation**
```csharp
namespace S13.DocMind.Services.Enhanced
{
    public class ImageUnderstandingService
    {
        private readonly ILogger<ImageUnderstandingService> _logger;

        public async Task<DocumentImage> AnalyzeImageAsync(Document document, CancellationToken ct = default)
        {
            if (!IsImageDocument(document))
                throw new InvalidOperationException("Document must be an image for diagram analysis");

            var base64Image = await GetBase64ImageAsync(document.StorageKey);

            var diagramPrompt = $"""
                COMPREHENSIVE DIAGRAM ANALYSIS

                Analyze this diagram and extract:
                1. SUMMARY: What the diagram represents
                2. FLOW_STEPS: Sequential process steps
                3. KEY_SERVICES: Components and their roles
                4. SECURITY_MECHANISMS: Security features visible
                5. RISKS: Potential vulnerabilities or issues
                6. GRAPH: Structured node/edge representation

                JSON Response:
                {{
                    "summary": "Diagram overview",
                    "flowSteps": ["Step 1", "Step 2"],
                    "keyServices": [{{ "name": "Service", "role": "Function", "interactions": "Connections" }}],
                    "securityMechanisms": ["Auth mechanism"],
                    "risks": ["Risk description"],
                    "diagramGraph": {{
                        "nodes": [{{ "id": "node1", "label": "Label", "category": "service" }}],
                        "edges": [{{ "from": "node1", "to": "node2", "type": "flow" }}]
                    }}
                }}
                """;

            var response = await AI.VisionPrompt(diagramPrompt, base64Image)
                .WithModel("gpt-4-vision-preview")
                .WithMaxTokens(4000)
                .WithTemperature(0.1)
                .ExecuteAsync(ct);

            var image = new DocumentImage
            {
                DocumentId = document.Id,
                RawLlmResponse = response.Content,
                ModelUsed = response.Model ?? "gpt-4-vision-preview",
                ProcessingDuration = response.ProcessingTime ?? TimeSpan.Zero
            };

            try
            {
                var result = JsonSerializer.Deserialize<DiagramAnalysisResult>(response.Content);
                if (result != null)
                {
                    diagram.Summary = result.Summary;
                    diagram.FlowSteps = result.FlowSteps;
                    diagram.KeyServices = result.KeyServices;
                    diagram.SecurityMechanisms = result.SecurityMechanisms;
                    diagram.Risks = result.Risks;
                    diagram.DiagramGraphJson = JsonSerializer.Serialize(result.DiagramGraph);
                    diagram.ConfidenceScore = 0.85;
                }
            }
            catch (JsonException)
            {
                diagram.Summary = ExtractSummaryFromResponse(response.Content);
                diagram.ConfidenceScore = 0.6;
            }

            await diagram.Save(ct);
            return diagram;
        }

        private bool IsImageDocument(Document document) =>
            document.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        private async Task<string> GetBase64ImageAsync(string storageKey)
        {
            // Implementation depends on storage provider
            // Placeholder for actual storage retrieval
            return "base64_encoded_image_data";
        }

        private string ExtractSummaryFromResponse(string response)
        {
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0] : "Diagram analysis completed";
        }
    }

    internal class DiagramAnalysisResult
    {
        public string Summary { get; set; } = "";
        public List<string> FlowSteps { get; set; } = new();
        public List<KeyService> KeyServices { get; set; } = new();
        public List<string> SecurityMechanisms { get; set; } = new();
        public List<string> Risks { get; set; } = new();
        public DiagramGraph DiagramGraph { get; set; } = new();
    }
}
```

#### **Document Type Auto-Matching Service**
```csharp
namespace S13.DocMind.Services.Enhanced
{
    public class DocumentTypeMatchingService
    {
        private readonly ILogger<DocumentTypeMatchingService> _logger;

        public async Task<List<TypeMatchResult>> SuggestTypesAsync(
            Document document, int maxSuggestions = 3)
        {
            if (string.IsNullOrWhiteSpace(document.ExtractedText))
                return new List<TypeMatchResult>();

            // Generate embedding for semantic matching
            var contentEmbedding = await AI.Embed(document.ExtractedText).ExecuteAsync();

            // Get all enabled document types
            var documentTypes = await DocumentType.Query(dt => dt.EnableAutoMatching).All();
            var matches = new List<TypeMatchResult>();

            foreach (var docType in documentTypes)
            {
                var matchResult = await CalculateTypeMatchAsync(
                    document.ExtractedText, contentEmbedding, docType);

                if (matchResult.Confidence >= docType.ConfidenceThreshold)
                {
                    matches.Add(matchResult);
                }
            }

            return matches
                .OrderByDescending(m => m.Confidence)
                .Take(maxSuggestions)
                .ToList();
        }

        private async Task<TypeMatchResult> CalculateTypeMatchAsync(
            string content, double[] contentEmbedding, DocumentType docType)
        {
            // Semantic similarity using embeddings
            double semanticSimilarity = 0.0;
            if (docType.TypeEmbedding?.Length > 0)
            {
                semanticSimilarity = CalculateCosineSimilarity(contentEmbedding, docType.TypeEmbedding);
            }

            // Keyword matching
            var keywordMatches = GetMatchingKeywords(content, docType.KeywordTriggers);
            double keywordSimilarity = docType.KeywordTriggers.Count > 0
                ? (double)keywordMatches.Count / docType.KeywordTriggers.Count
                : 0.0;

            // Weighted confidence calculation
            var confidence = (semanticSimilarity * 0.7) + (keywordSimilarity * 0.3);

            return new TypeMatchResult
            {
                DocumentType = docType,
                Confidence = confidence,
                SemanticSimilarity = semanticSimilarity,
                KeywordSimilarity = keywordSimilarity,
                KeywordMatches = keywordMatches,
                Reasoning = $"Semantic: {semanticSimilarity:P1}, Keywords: {keywordSimilarity:P1}"
            };
        }

        private List<string> GetMatchingKeywords(string content, List<string> keywords)
        {
            var lowerContent = content.ToLowerInvariant();
            return keywords.Where(k => lowerContent.Contains(k.ToLowerInvariant())).ToList();
        }

        private static double CalculateCosineSimilarity(double[] vector1, double[] vector2)
        {
            if (vector1.Length != vector2.Length) return 0.0;

            var dotProduct = vector1.Zip(vector2, (a, b) => a * b).Sum();
            var magnitude1 = Math.Sqrt(vector1.Sum(x => x * x));
            var magnitude2 = Math.Sqrt(vector2.Sum(x => x * x));

            return magnitude1 * magnitude2 == 0 ? 0.0 : dotProduct / (magnitude1 * magnitude2);
        }
    }

    public class TypeMatchResult
    {
        public DocumentType DocumentType { get; set; } = null!;
        public double Confidence { get; set; }
        public double SemanticSimilarity { get; set; }
        public double KeywordSimilarity { get; set; }
        public List<string> KeywordMatches { get; set; } = new();
        public string Reasoning { get; set; } = "";
    }
}
```

#### **Document Chunking Service Implementation**
```csharp
namespace S13.DocMind.Services.Enhanced
{
    public class DocumentChunkingService
    {
        private readonly ILogger<DocumentChunkingService> _logger;
        private readonly EnhancedDocumentAnalysisService _analysisService;

        public async Task<List<DocumentChunk>> ChunkDocumentAsync(
            Document document, CancellationToken ct = default)
        {
            if (document.ExtractedText.Length <= document.MaxChunkSize)
            {
                _logger.LogInformation("Document {DocumentId} does not require chunking", document.Id);
                return new List<DocumentChunk>();
            }

            var chunks = SplitIntoChunks(document.ExtractedText, (int)document.MaxChunkSize);
            var chunkEntities = new List<DocumentChunk>();

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = new DocumentChunk
                {
                    DocumentId = document.Id,
                    ChunkIndex = i,
                    Content = chunks[i].Content,
                    StartPosition = chunks[i].StartPosition,
                    EndPosition = chunks[i].EndPosition
                };

                await chunk.Save(ct);
                chunkEntities.Add(chunk);
            }

            // Update document chunking status
            document.IsChunked = true;
            document.ChunkCount = chunks.Count;
            await document.Save(ct);

            return chunkEntities;
        }

        public async Task<List<DocumentChunk>> AnalyzeChunksAsync(
            Document document, DocumentType docType, CancellationToken ct = default)
        {
            var chunks = await DocumentChunk.Query(c => c.DocumentId == document.Id).All();

            foreach (var chunk in chunks.Where(c => string.IsNullOrEmpty(c.ChunkAnalysis)))
            {
                try
                {
                    // Analyze chunk with same document type
                    var chunkDoc = new Document
                    {
                        ExtractedText = chunk.Content,
                        ContentType = document.ContentType
                    };

                    var extractedInfo = await _analysisService.AnalyzeDocumentStructuredAsync(chunkDoc, docType, ct);

                    chunk.ExtractedInformation = extractedInfo;
                    chunk.ConfidenceScore = extractedInfo.ConfidenceScore;
                    chunk.ChunkAnalysis = extractedInfo.Summary;
                    chunk.AnalyzedAt = DateTime.UtcNow;

                    await chunk.Save(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to analyze chunk {ChunkIndex} for document {DocumentId}",
                        chunk.ChunkIndex, document.Id);
                }
            }

            return chunks;
        }

        private List<ChunkData> SplitIntoChunks(string text, int maxSize, int overlap = 500)
        {
            var chunks = new List<ChunkData>();
            int position = 0;

            while (position < text.Length)
            {
                int end = Math.Min(position + maxSize, text.Length);

                // Break at word boundaries when possible
                if (end < text.Length)
                {
                    var lastSpace = text.LastIndexOf(' ', end, Math.Min(200, end - position));
                    if (lastSpace > position) end = lastSpace;
                }

                chunks.Add(new ChunkData(
                    text[position..end],
                    position,
                    end
                ));

                position = end - overlap;
                if (position >= end) break; // Prevent infinite loop
            }

            return chunks;
        }

        private record ChunkData(string Content, int StartPosition, int EndPosition);
    }
}
```

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
    // In-memory caching handled by framework, cache entity not needed in simplified approach
    // public sealed class ProcessingCache : Entity<ProcessingCache>
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
    // Audit logs stored in MongoDB with other entities
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
