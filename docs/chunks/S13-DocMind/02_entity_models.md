## **S13.DocMind Architecture**

### **1. Entity-First Data Models**

#### **File Entity (Individual Document + Embedded Analysis)**
```csharp
[DataAdapter("mongodb")] // Document storage optimized for file metadata
[McpEntity(
    Name = "documents",
    Description = "Document intelligence and processing system",
    RequiredScopes = new[] { "documents:read", "documents:write" },
    EnableMutations = true
)]
public sealed class File : Entity<File>
{
    // Raw file metadata
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSize { get; set; }
    public string Sha512Hash { get; set; } = "";

    // Storage references
    public string? StorageBucket { get; set; }
    public string? StorageKey { get; set; }

    // Extracted content (ready for AI processing)
    public string ExtractedText { get; set; } = "";
    public ExtractionMethod ExtractionMethod { get; set; }
    public DateTime? ExtractedAt { get; set; }

    // User-assigned type (trigger for AI processing)
    [Parent(typeof(Type))]
    public Guid? TypeId { get; set; }
    public DateTime? TypeAssignedAt { get; set; }

    // EMBEDDED individual analysis results (replaces separate Analysis entity)
    public string? AnalysisResult { get; set; } = "";       // AI-generated analysis (legacy)
    public string? FilledTemplate { get; set; } = "";        // Template-formatted result
    public double? ConfidenceScore { get; set; }
    public DateTime? AnalyzedAt { get; set; }
    public string? ModelUsed { get; set; } = "";
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public TimeSpan? ProcessingDuration { get; set; }

    // ENHANCED: Rich structured analysis results (GDoc feature parity)
    public ExtractedDocumentInformation? ExtractedInformation { get; set; }

    // UX enhancements (from GDoc analysis)
    public string? UserFileName { get; set; }              // User-friendly display name
    public string Notes { get; set; } = "";                // Per-file user context

    // Auto-classification features
    public double DocumentTypeMatchConfidence { get; set; } = 0.0;
    public bool IsAutoClassified { get; set; } = false;

    // Processing state tracking (enhanced from GDoc)
    public FileState State { get; set; } = FileState.Uploaded;
    public bool IsContentProcessed { get; set; } = false;   // Text extraction complete
    public bool IsExtractionComplete { get; set; } = false; // Analysis complete
    public Guid? FileSpecificAnalysisId { get; set; }       // Auto-analysis link
    public string? ProcessingError { get; set; }

    // Document chunking support (for large files)
    public bool IsChunked { get; set; } = false;
    public int ChunkCount { get; set; } = 0;
    public long MaxChunkSize { get; set; } = 32000; // Characters

    // Child relationships
    public async Task<List<DocumentChunk>> GetChunks() => await GetChildren<DocumentChunk>();
    public async Task<DocumentImage?> GetImage() =>
        await DocumentImage.Query(d => d.FileId == Id).FirstOrDefault();
}

public enum FileState
{
    Uploaded,           // File uploaded, content extracted, awaiting type assignment
    TypeAssigned,       // User assigned type, analysis queued
    Analyzing,          // AI analysis in progress
    Analyzed,           // Analysis completed successfully
    AnalysisFailed      // Analysis failed with error
}

public enum ExtractionMethod
{
    None,        // Text files - no extraction needed
    PdfParser,   // PDF → text extraction
    OcrEngine,   // Images → OCR text
    DocxParser,  // DOCX → text extraction
    Custom       // Extensible for other formats
}
```

The **corrected** user-driven processing workflow follows GDoc's actual pattern:

1. **Analysis Request Creation**: User creates an Analysis entity (multi-document project)
2. **File Upload & Extraction**: Files are uploaded with content extraction (PDF→text, OCR, etc.)
3. **Individual File Analysis**: Users assign Type to each file → AI analyzes each file individually → results stored in `File.AnalysisResult`
4. **Request-Level Analysis**: Once all files analyzed individually, user triggers Analysis → AI combines individual analyses → generates final aggregated result

**Key Workflow Notes:**
- **Individual analysis first**: Each file gets its own AI analysis stored directly on the File entity
- **Aggregation second**: The Analysis entity coordinates multi-document analysis using individual results
- **GDoc pattern preserved**: Multi-document analysis with templated output, not just single-file processing
- **Embedded results**: No separate Analysis entities for individual files - results embedded in File entities

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

**Key Design Principles:**

- **Simplified Infrastructure**: MongoDB for all entities, Ollama for AI, optional Weaviate for embeddings only
- **Embedded Analysis Results**: Individual file analysis stored directly on File entities (no separate Analysis entities)
- **Analysis as Request Coordinator**: Analysis entity manages multi-document workflows and aggregated results
- **Data Locality**: Analysis results stay with their files for better performance and simpler queries
- **GDoc Flow Preservation**: Multi-document analysis with individual file processing, matching original GDoc patterns
- **Externalized Storage**: File content stored separately, only metadata in MongoDB for lean document storage

#### **Type Entity (Document Classification + AI Instructions)**
```csharp
[McpEntity(
    Name = "document-types",
    Description = "Document type templates and classification system",
    RequiredScopes = new[] { "documents:read", "documents:configure" },
    EnableMutations = true
)]
public sealed class Type : Entity<Type>
{
    // Classification info
    public string Name { get; set; } = "";           // "Meeting Transcript"
    public string Code { get; set; } = "";           // "MEETING"
    public string Description { get; set; } = "";
    public List<string> Tags { get; set; } = new();

    // AI extraction instructions
    public string ExtractionPrompt { get; set; } = "";     // How to analyze content
    public string TemplateStructure { get; set; } = "";    // Output format template
    public string Examples { get; set; } = "";             // Few-shot examples

    // Auto-classification hints (enhanced from GDoc analysis)
    public List<string> KeywordTriggers { get; set; } = new();
    public double ConfidenceThreshold { get; set; } = 0.7;
    public bool EnableAutoMatching { get; set; } = true;

    // Vector matching for type classification
    [Vector(Dimensions = 1536)]
    public double[]? TypeEmbedding { get; set; }

    // Usage analytics (from GDoc analysis)
    public int UsageCount { get; set; } = 0;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
    public double AverageConfidenceScore { get; set; } = 0.0;

    // Child relationships - files assigned to this type
    public async Task<List<File>> GetFiles() => await GetChildren<File>();
}
```

#### **Analysis Entity (Multi-Document Request + Aggregated Results)**
```csharp
[DataAdapter("mongodb")] // Multi-document analysis coordination
[McpEntity(
    Name = "document-requests",
    Description = "Multi-document analysis coordination and aggregation",
    RequiredScopes = new[] { "documents:read", "documents:analyze" },
    EnableMutations = true
)]
public sealed class Analysis : Entity<Analysis>
{
    // Request-level properties (DocumentRequest equivalent)
    public string Name { get; set; } = "";                      // "Q3 Financial Analysis"
    public string Description { get; set; } = "";               // User description of analysis purpose
    public List<Guid> FileIds { get; set; } = new();            // Multiple files to analyze together
    public string? RequestedBy { get; set; }

    // Template for final aggregated output
    [Parent(typeof(Type))]
    public Guid? TypeId { get; set; }                           // Template for final analysis

    // FINAL aggregated analysis (generated from individual file analyses)
    public string? FinalAnalysis { get; set; } = "";            // Multi-document analysis result
    public string? FilledTemplate { get; set; } = "";           // Final template output
    public double? ConfidenceScore { get; set; }
    public DateTime? AnalyzedAt { get; set; }
    public string? ModelUsed { get; set; } = "";
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public TimeSpan? ProcessingDuration { get; set; }

    // Request processing state
    public AnalysisState State { get; set; } = AnalysisState.Created;
    public string? ProcessingError { get; set; }

    // Helper method to get associated files
    public async Task<List<File>> GetFiles() =>
        await File.Query(f => FileIds.Contains(f.Id));
}

public enum AnalysisState
{
    Created,        // Request created, files being uploaded/analyzed individually
    ReadyToAnalyze, // All files have individual analyses, ready for aggregation
    Analyzing,      // Final aggregated analysis in progress
    Completed,      // Final analysis completed successfully
    Failed          // Analysis failed with error
}
```

---

### **Enhanced Entity Models (GDoc Feature Parity)**

#### **Rich Structured Document Information**
```csharp
/// <summary>
/// Represents comprehensive structured information extracted from documents.
/// Replaces simple string-based analysis with rich, searchable data.
/// Matches GDoc's ExtractedDocumentInformation capabilities.
/// </summary>
public class ExtractedDocumentInformation
{
    /// <summary>
    /// Key entities found in the document (people, organizations, dates, etc.)
    /// </summary>
    public Dictionary<string, List<string>> Entities { get; set; } = new();

    /// <summary>
    /// Main topics and themes identified in the document.
    /// </summary>
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// Document type/category inferred from content (meeting notes, technical spec, etc.)
    /// </summary>
    public string InferredDocumentType { get; set; } = "";

    /// <summary>
    /// Key facts and data points extracted from the document.
    /// </summary>
    public List<KeyFact> KeyFacts { get; set; } = new();

    /// <summary>
    /// Structured data fields found in the document.
    /// </summary>
    public Dictionary<string, object> StructuredData { get; set; } = new();

    /// <summary>
    /// Summary of the document's main content and purpose.
    /// </summary>
    public string Summary { get; set; } = "";

    /// <summary>
    /// Overall confidence score for the extraction (0.0 to 1.0).
    /// </summary>
    public double ConfidenceScore { get; set; } = 0.0;

    /// <summary>
    /// Timestamp when this information was extracted.
    /// </summary>
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Model used for extraction.
    /// </summary>
    public string ModelUsed { get; set; } = "";
}

/// <summary>
/// Represents a key fact extracted from a document with confidence and context.
/// </summary>
public class KeyFact
{
    /// <summary>
    /// The type/category of the fact (e.g., "decision", "action_item", "requirement").
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// The factual information.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Confidence score for this specific fact (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; set; } = 0.0;

    /// <summary>
    /// Context or source within the document where this fact was found.
    /// </summary>
    public string Context { get; set; } = "";
}
```

#### **Document Image Understanding Entity**
```csharp
/// <summary>
/// Represents visual content analysis for any image content in documents.
/// Handles diagrams, charts, photos, screenshots, and other visual elements.
/// Provides comprehensive image understanding matching GDoc's capabilities.
/// </summary>
[DataAdapter("mongodb")]
[McpEntity(
    Name = "document-images",
    Description = "Visual content analysis and image understanding",
    RequiredScopes = new[] { "documents:read", "documents:analyze" }
)]
public sealed class DocumentImage : Entity<DocumentImage>
{
    [Parent(typeof(File))]
    public Guid FileId { get; set; }

    // Analysis results
    public string Summary { get; set; } = "";
    public List<string> FlowSteps { get; set; } = new();
    public List<KeyService> KeyServices { get; set; } = new();
    public List<string> SecurityMechanisms { get; set; } = new();
    public List<string> Risks { get; set; } = new();

    // Graph representation (structured extraction)
    public string DiagramGraphJson { get; set; } = "";
    public string RawLlmResponse { get; set; } = "";

    // Processing metadata
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public string ModelUsed { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
    public double ConfidenceScore { get; set; } = 0.0;

    // Helper method to get structured graph
    public DiagramGraph? GetStructuredGraph()
    {
        if (string.IsNullOrEmpty(DiagramGraphJson)) return null;
        return JsonSerializer.Deserialize<DiagramGraph>(DiagramGraphJson);
    }

    // Generate markdown summary
    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Diagram Understanding Summary");
        if (!string.IsNullOrWhiteSpace(Summary)) sb.AppendLine(Summary.Trim()).AppendLine();

        if (FlowSteps.Count > 0)
        {
            sb.AppendLine("## Flow Steps");
            for (int i = 0; i < FlowSteps.Count; i++) sb.AppendLine($"{i+1}. {FlowSteps[i]}");
            sb.AppendLine();
        }

        if (KeyServices.Count > 0)
        {
            sb.AppendLine("## Key Services");
            sb.AppendLine("Name | Role | Interactions");
            sb.AppendLine("---|---|---");
            foreach (var s in KeyServices) sb.AppendLine($"{s.Name} | {s.Role} | {s.Interactions}");
            sb.AppendLine();
        }

        if (SecurityMechanisms.Count > 0)
        {
            sb.AppendLine("## Security Mechanisms");
            foreach (var s in SecurityMechanisms) sb.AppendLine("- " + s);
            sb.AppendLine();
        }

        if (Risks.Count > 0)
        {
            sb.AppendLine("## Potential Risks");
            foreach (var r in Risks) sb.AppendLine("- " + r);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// Represents a service or component identified in a diagram.
/// </summary>
public record KeyService(string Name, string Role, string Interactions);

/// <summary>
/// Represents the structured graph extracted from a diagram.
/// </summary>
public class DiagramGraph
{
    public List<DiagramNode> Nodes { get; set; } = new();
    public List<DiagramEdge> Edges { get; set; } = new();
    public List<DiagramGroup> Groups { get; set; } = new();
    public List<string> Notes { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = false });
}

public class DiagramNode
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Category { get; set; } = "";
    public string Shape { get; set; } = "";
}

public class DiagramEdge
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Type { get; set; } = "flow";
    public string? Label { get; set; }
}

public class DiagramGroup
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string BoundaryType { get; set; } = "";
    public List<string> Nodes { get; set; } = new();
}
```

#### **Document Chunking Support**
```csharp
/// <summary>
/// Represents a chunk of a large document for processing.
/// Enables handling of files that exceed LLM context windows.
/// </summary>
[DataAdapter("mongodb")]
[McpEntity(
    Name = "document-chunks",
    Description = "Large document chunking and processing",
    RequiredScopes = new[] { "documents:read", "documents:analyze" }
)]
public sealed class DocumentChunk : Entity<DocumentChunk>
{
    [Parent(typeof(File))]
    public Guid FileId { get; set; }

    // Chunk positioning
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = "";
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }

    // Individual chunk analysis results
    public string? ChunkAnalysis { get; set; }
    public ExtractedDocumentInformation? ExtractedInformation { get; set; }
    public double? ConfidenceScore { get; set; }

    // Processing metadata
    public DateTime? AnalyzedAt { get; set; }
    public string? ModelUsed { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public TimeSpan? ProcessingDuration { get; set; }
}
```

#### **Document Type Matching Support**
```csharp
/// <summary>
/// Represents the result of automatic document type matching.
/// Used by auto-classification services to suggest appropriate types.
/// </summary>
public class TypeMatchResult
{
    public Type Type { get; set; } = null!;
    public double Confidence { get; set; } = 0.0;
    public List<string> KeywordMatches { get; set; } = new();
    public double SemanticSimilarity { get; set; } = 0.0;
    public double KeywordSimilarity { get; set; } = 0.0;
    public string Reasoning { get; set; } = "";
}

/// <summary>
/// Configuration for automatic document type classification.
/// </summary>
public class AutoClassificationConfig
{
    public bool EnableAutoClassification { get; set; } = true;
    public int MaxSuggestions { get; set; } = 3;
    public double MinConfidenceThreshold { get; set; } = 0.6;
    public double SemanticWeight { get; set; } = 0.7;
    public double KeywordWeight { get; set; } = 0.3;
}
```

