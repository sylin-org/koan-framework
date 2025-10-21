# S7.Meridian - Document Intelligence System

**Navigate documents to truth.**

A local-first document intelligence workbench that transforms mixed source files (PDFs, documents, images) into evidence-backed deliverables with minimal effort. Upload files, define what you want to create, and Meridian distills the evidence into trustworthy, cited documents.

---

## What This Sample Demonstrates

### 1. **Evidence-First Document Intelligence**
- **Automated extraction**: Parse PDFs, extract structured data, cite every value
- **Source classification**: Auto-detect document types (questionnaires, financial statements, etc.)
- **Multi-source aggregation**: Merge data from multiple files with conflict resolution
- **Citation tracking**: Every extracted value links back to source page and text span

### 2. **Koan Framework Patterns**
- **Entity<T> first design**: All domain models use `Entity<T>` with auto GUID v7
- **Canon Runtime pipelines**: Processing workflow as `CanonEntity<T>` with phase contributors
- **EntityController<T>**: Auto-generated REST APIs with minimal custom code
- **Multi-provider data**: MongoDB for documents, Weaviate for vector search
- **Content-addressed storage**: Files stored by SHA-512 hash (S6.SnapVault pattern)

### 3. **Local-First AI Integration**
- **Ollama**: Local LLMs for extraction, classification, type generation
- **Weaviate**: Hybrid vector search for passage retrieval
- **Schema-driven extraction**: JSON Schema validation prevents hallucination
- **Confidence scoring**: Every value has evidence-based confidence metrics

### 4. **Production-Ready Patterns**
- **Background processing**: Hosted service with graceful shutdown
- **Pipeline orchestration**: Canon phases (Validation → Enrichment)
- **Error handling**: Retry logic, partial failures, recovery
- **Observability**: Structured logging, metrics, phase tracking

---

## The Problem It Solves

**Scenario**: You need to respond to an RFP or create a vendor assessment. You have:
- 5 PDF documents (prescreen questionnaire, financial statements, compliance certs, references)
- Each document has overlapping information (company name, revenue, employee count)
- Different formats, different data quality, sometimes conflicting values
- You need to produce a single, trustworthy deliverable with citations

**Manual approach** (4-6 hours):
1. Open each PDF
2. Find relevant sections
3. Copy values into template
4. Cross-check conflicts
5. Lose track of where data came from
6. Second-guess yourself
7. Format final document

**Meridian approach** (10-15 minutes):
1. Upload files → auto-classified
2. Click "Process" → pipeline runs
3. Review conflicts → select correct values with evidence
4. Click "Finalize" → PDF ready with full citations

---

## Quick Start

### Prerequisites

- .NET 10 SDK
- Docker Desktop (for MongoDB, Weaviate, Ollama)
- Optional: set `WEAVIATE_ENDPOINT` to reuse an existing Weaviate instance (defaults to the Koan test fixture)
- 16GB RAM minimum (for local LLMs)

### Run the Sample

```bash
cd samples/S7.Meridian
./start.bat  # Windows
# or
./start.sh   # Linux/Mac
```

This will:
1. Start containers (MongoDB, Weaviate, Ollama via Compose)
2. Pull recommended Ollama models
3. Seed sample types (Vendor Assessment, RFP Response)
4. Run self-test
5. Open browser to http://localhost:5104

### Validate Vector Workflows

Before wiring new flows, rerun the vector connector spec to ensure the shared profile remains green:

```bash
dotnet test tests/Suites/Data/Connector.Weaviate/Koan.Data.Connector.Weaviate.Tests/ -c Release --filter "FullyQualifiedName~WeaviateConnectorSpec"
```

When the Meridian regression spec lands, it will live under `tests/Suites/Data/S7.Meridian/Koan.Data.S7.Meridian.Tests/`. Target it directly to validate the `meridian:evidence` profile:

```bash
dotnet test tests/Suites/Data/S7.Meridian/Koan.Data.S7.Meridian.Tests/ -c Release --filter MeridianVectorWorkflowSpec
```

### First Analysis

1. **Choose deliverable type**: "Vendor Assessment"
2. **Add context**: "Prioritize Q3 2024 data"
3. **Upload files**: Drag PDFs (auto-classified)
4. **Process**: Watch pipeline stages
5. **Review**: Resolve conflicts with evidence drawer
6. **Finalize**: Download PDF with citations

---

## How to Build an App Like This

### Step 1: Define Your Domain Model

Start with `Entity<T>` for all domain concepts:

```csharp
// Source Type = per-file extractor
public class SourceType : Entity<SourceType>
{
    public string Name { get; set; } = "";
    public JsonDocument JsonSchema { get; set; } = null!;
    public SourceDiscriminators Discriminators { get; set; } = new();
    public RetrievalHints RetrievalHints { get; set; } = new();
}

// Deliverable Type = final document schema + template
public class DeliverableType : Entity<DeliverableType>
{
    public string Name { get; set; } = "";
    public JsonDocument JsonSchema { get; set; } = null!;
    public string TemplateMd { get; set; } = ""; // Mustache template
    public MergeRules MergeRules { get; set; } = new();
}

// Analysis Request = one deliverable with many files
public class AnalysisRequest : Entity<AnalysisRequest>
{
    public string DeliverableTypeId { get; set; } = "";
    public string Notes { get; set; } = ""; // User guidance
    public RequestStatus Status { get; set; }

    // Navigation
    public async Task<List<SourceFile>> GetSourceFiles()
        => await SourceFile.Query(q => q.Where(f => f.RequestId == Id));
}

// Source File = uploaded document with classification
public class SourceFile : Entity<SourceFile>
{
    public string RequestId { get; set; } = "";
    public string SourceTypeId { get; set; } = "";
    public string ContentHash { get; set; } = ""; // SHA-512
    public ClassificationResult Classification { get; set; } = new();
    public string StoragePath { get; set; } = "";
}

// Passage = evidence atom (chunk of text with vector)
public class Passage : Entity<Passage>
{
    public string SourceFileId { get; set; } = "";
    public int PageNumber { get; set; }
    public string Text { get; set; } = "";
    public string VectorId { get; set; } = ""; // Weaviate reference
}

// Deliverable Field = aggregated value with citations
public class DeliverableField : Entity<DeliverableField>
{
    public string RequestId { get; set; } = "";
    public string FieldPath { get; set; } = ""; // e.g., "annualRevenue"
    public JsonDocument SelectedValue { get; set; } = null!;
    public List<FieldCandidate> Candidates { get; set; } = new();
    public List<Citation> Citations { get; set; } = new();
    public double Confidence { get; set; }
    public FieldStatus Status { get; set; }
}
```

**Key Insight**: Rich value objects (not primitive obsession). `Citation`, `ClassificationResult`, `MergeRules` are strongly typed.

---

### Step 2: Use EntityController<T> for REST APIs

Let Koan generate 80% of your API:

```csharp
[Route("api/[controller]")]
public class SourceTypesController : EntityController<SourceType>
{
    // Inherits: GET, POST, PUT, DELETE, Batch operations

    // Custom: Generate with AI
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateWithAI([FromBody] TypeGenerationRequest req)
    {
        var draft = await _aiService.GenerateSourceType(req);
        return Ok(draft);
    }
}

[Route("api/[controller]")]
public class AnalysisRequestsController : EntityController<AnalysisRequest>
{
    // Upload files
    [HttpPost("{id}/files")]
    public async Task<IActionResult> UploadFiles(string id, [FromForm] List<IFormFile> files)
    {
        var request = await AnalysisRequest.Get(id);
        var results = new List<SourceFile>();

        foreach (var file in files)
        {
            // Hash, store, auto-classify
            var sourceFile = await _fileService.IngestFile(request, file);
            results.Add(sourceFile);
        }

        return Ok(results);
    }

    // Start processing
    [HttpPost("{id}/process")]
    public async Task<IActionResult> Process(string id)
    {
        await _orchestrator.QueueProcessing(id);
        return Accepted();
    }
}
```

**Result**: Full CRUD + custom endpoints in ~50 lines of code.

---

### Step 3: Model Your Pipeline as Canon Workflow

The processing pipeline (Ingest → Parse → Extract → Aggregate → Render) is a `CanonEntity<T>`:

```csharp
public class AnalysisWorkflow : CanonEntity<AnalysisWorkflow>
{
    public string RequestId { get; set; } = "";
    public PipelineStage CurrentStage { get; set; }
    public Dictionary<string, StageResult> StageResults { get; set; } = new();
}

public enum PipelineStage
{
    Ingest,
    Classify,
    Parse,
    Embed,
    Extract,
    Aggregate,
    Render
}
```

**Benefit**: Canon provides orchestration, retries, phase tracking for free.

---

### Step 4: Implement Phase Contributors

Each pipeline stage is a `ICanonPhaseContributor<T>`:

```csharp
// Validation phase: Check file sizes, types
public class IngestValidationContributor : ICanonPhaseContributor<AnalysisWorkflow>
{
    public CanonPhase Phase => CanonPhase.Validation;

    public async Task<CanonResult> ContributeAsync(AnalysisWorkflow workflow)
    {
        var request = await AnalysisRequest.Get(workflow.RequestId);
        var files = await request.GetSourceFiles();

        foreach (var file in files)
        {
            if (file.SizeBytes > 50 * 1024 * 1024)
                return CanonResult.Reject($"File {file.OriginalName} exceeds 50MB");
        }

        return CanonResult.Accept();
    }
}

// Enrichment phase: Parse PDFs and chunk
public class ParseEnrichmentContributor : ICanonPhaseContributor<AnalysisWorkflow>
{
    public CanonPhase Phase => CanonPhase.Enrichment;

    public async Task<CanonResult> ContributeAsync(AnalysisWorkflow workflow)
    {
        var files = await GetSourceFiles(workflow.RequestId);

        foreach (var file in files)
        {
            // Parse PDF → text
            var parseResult = await _pdfParser.ParseAsync(file.StoragePath);

            // Chunk into passages (900 chars, 10% overlap)
            var passages = _chunker.Chunk(parseResult.Text, 900, 90);

            // Save passages
            foreach (var (text, page) in passages)
            {
                var passage = new Passage {
                    SourceFileId = file.Id,
                    PageNumber = page,
                    Text = text
                };
                await passage.Save();
            }
        }

        return CanonResult.Accept();
    }
}

// Enrichment phase: Embed and index
public class EmbedEnrichmentContributor : ICanonPhaseContributor<AnalysisWorkflow>
{
    public CanonPhase Phase => CanonPhase.Enrichment;

    public async Task<CanonResult> ContributeAsync(AnalysisWorkflow workflow)
    {
        var files = await GetSourceFiles(workflow.RequestId);

        foreach (var file in files)
        {
            var passages = await file.GetPassages();

            // Batch embed (following S5.Recs pattern)
            var embeddings = await _embeddingService.EmbedBatchAsync(
                passages.Select(p => p.Text).ToList()
            );

            // Upsert to Weaviate
            for (int i = 0; i < passages.Count; i++)
            {
                var vectorId = await _vectorStore.UpsertAsync(new VectorDocument {
                    Id = passages[i].Id,
                    Text = passages[i].Text,
                    Embedding = embeddings[i],
                    Metadata = new { sourceFileId = file.Id, page = passages[i].PageNumber }
                });

                passages[i].VectorId = vectorId;
                await passages[i].Save();
            }
        }

        return CanonResult.Accept();
    }
}
```

**Auto-Registration**: Contributors are discovered and registered automatically.

---

### Step 5: Extract with Schema Validation

Use JSON Schema to prevent hallucination:

```csharp
public class ExtractEnrichmentContributor : ICanonPhaseContributor<AnalysisWorkflow>
{
    public async Task<CanonResult> ContributeAsync(AnalysisWorkflow workflow)
    {
        var sourceType = await GetSourceType(workflow);
        var schema = sourceType.JsonSchema;

        foreach (var field in GetSchemaFields(schema))
        {
            // Retrieve relevant passages (hybrid search)
            var passages = await _vectorStore.HybridSearchAsync(
                query: field.RetrievalHints.Keywords.Join(" "),
                filter: new { sourceFileId = file.Id },
                topK: 12
            );

            // Extract with LLM
            var extraction = await _extractionService.ExtractFieldAsync(
                fieldName: field.Name,
                fieldSchema: field.Schema, // Subschema for this field
                context: passages.Select(p => p.Text),
                hints: sourceType.RetrievalHints
            );

            // Validate against schema (CRITICAL: prevents garbage data)
            if (!_validator.IsValid(extraction.Value, field.Schema))
            {
                return CanonResult.Warn($"Invalid value for {field.Name}");
            }

            // Save with citations
            var sourceExtraction = new SourceExtraction {
                SourceFileId = file.Id,
                FieldPath = field.Name,
                ExtractedValue = extraction.Value,
                Citations = extraction.Citations, // Passage IDs + spans
                Confidence = extraction.Confidence
            };
            await sourceExtraction.Save();
        }

        return CanonResult.Accept();
    }
}
```

**Key Technique**: Pass the **field-specific JSON Schema** to the LLM. Prompt includes:
- Field name and description
- Expected type (string, number, date, enum, etc.)
- Validation rules (min/max, pattern, required)
- Context passages

LLM returns JSON that MUST validate before acceptance.

---

### Step 6: Aggregate with Merge Rules

Handle conflicts when multiple sources provide the same field:

```csharp
public class AggregateEnrichmentContributor : ICanonPhaseContributor<AnalysisWorkflow>
{
    public async Task<CanonResult> ContributeAsync(AnalysisWorkflow workflow)
    {
        var deliverableType = await GetDeliverableType(workflow);
        var schema = deliverableType.JsonSchema;

        foreach (var field in GetSchemaFields(schema))
        {
            // Collect all source extractions
            var candidates = await GetCandidatesForField(workflow.RequestId, field.Name);

            // Apply merge rule
            var mergeRule = deliverableType.MergeRules
                .GetStrategyForField(field.Name);

            var selected = _mergeService.ApplyMergeRule(candidates, mergeRule);

            // Determine status
            var status = FieldStatus.Approved;
            if (selected.Confidence < 0.7)
                status = FieldStatus.LowConfidence;
            else if (candidates.Count > 1 && HasConflicts(candidates))
                status = FieldStatus.Conflict;

            // Save deliverable field
            var deliverableField = new DeliverableField {
                RequestId = workflow.RequestId,
                FieldPath = field.Name,
                SelectedValue = selected.Value,
                Candidates = candidates, // Keep alternatives
                Citations = selected.Citations,
                Confidence = selected.Confidence,
                Status = status,
                MergeRuleUsed = mergeRule.ToString()
            };
            await deliverableField.Save();
        }

        return CanonResult.Accept();
    }
}
```

**Merge Strategies**:
- **Precedence**: First valid value wins (source type priority)
- **LatestDate**: Most recent value (when sources have timestamps)
- **Consensus**: Most common value (majority vote)
- **Highest/Lowest**: For numeric fields
- **Concatenate**: For text fields (join with separator)

---

### Step 7: Render with Mustache Templates

```csharp
public class RenderEnrichmentContributor : ICanonPhaseContributor<AnalysisWorkflow>
{
    public async Task<CanonResult> ContributeAsync(AnalysisWorkflow workflow)
    {
        var deliverableType = await GetDeliverableType(workflow);

        // Collect approved field values
        var fields = await DeliverableField.Query(q =>
            q.Where(f => f.RequestId == workflow.RequestId &&
                        f.Status == FieldStatus.Approved));

        var data = fields.ToDictionary(
            f => f.FieldPath,
            f => JsonSerializer.Deserialize<object>(f.SelectedValue)
        );

        // Render Markdown (Mustache template)
        var markdown = await _templateService.RenderMarkdown(
            deliverableType.TemplateMd,
            data
        );

        // Convert to PDF (Pandoc)
        var pdf = await _pandocService.ConvertToPdfAsync(markdown);

        // Save outputs
        await SaveOutput(workflow.RequestId, OutputFormat.Markdown, markdown);
        await SaveOutput(workflow.RequestId, OutputFormat.Pdf, pdf);

        return CanonResult.Accept();
    }
}
```

**Template Example**:
```markdown
# Vendor Assessment: {{companyName}}

## Executive Summary
- **Annual Revenue**: {{annualRevenue}}
- **Employees**: {{employeeCount}}
- **Certification Status**: {{certificationStatus}}

## Financial Health
Total revenue for {{fiscalYear}} was {{annualRevenue}}, representing
{{growthRate}}% growth over previous year.

## Citations
All data sourced from:
{{#sources}}
- {{filename}} (uploaded {{date}})
{{/sources}}
```

---

## Key Patterns Demonstrated

### 1. **Content-Addressed Storage** (S6.SnapVault Pattern)

```csharp
public async Task<string> SaveFileAsync(IFormFile file)
{
    // Compute SHA-512 hash while streaming
    using var stream = file.OpenReadStream();
    var hash = await ComputeSHA512Async(stream);

    // Store as hash.ext (deduplication automatic)
    var extension = Path.GetExtension(file.FileName);
    var path = $"{hash}{extension}";

    await _storage.SaveAsync(path, stream);
    return path;
}
```

**Benefits**:
- Automatic deduplication (same file uploaded twice = single storage)
- Immutable (hash never changes)
- Verifiable (can verify integrity later)

---

### 2. **Cascade Classification** (Heuristic → Vector → LLM)

```csharp
public async Task<ClassificationResult> ClassifyAsync(SourceFile file)
{
    var text = await ExtractTextPreview(file); // First 2 pages

    // Try 1: Exact heuristics (fast, cheap)
    foreach (var sourceType in await SourceType.All())
    {
        if (sourceType.Discriminators.RegexPatterns.Any(p => Regex.IsMatch(text, p)))
        {
            return new ClassificationResult {
                SourceTypeId = sourceType.Id,
                Confidence = 0.95,
                Method = ClassificationMethod.Heuristic,
                Rationale = "Matched regex pattern"
            };
        }
    }

    // Try 2: Vector similarity (medium cost)
    var embedding = await _embeddingService.EmbedAsync(text);
    var similar = await _vectorStore.FindSimilarTypesAsync(embedding, topK: 3);

    if (similar.First().Score > 0.85)
    {
        return new ClassificationResult {
            SourceTypeId = similar.First().SourceTypeId,
            Confidence = similar.First().Score,
            Method = ClassificationMethod.VectorSimilarity,
            Rationale = $"High similarity to '{similar.First().Name}'"
        };
    }

    // Try 3: LLM close-set (expensive, accurate)
    var llmChoice = await _llm.ClassifyAsync(text, similar.Take(3).ToList());

    return new ClassificationResult {
        SourceTypeId = llmChoice.SourceTypeId,
        Confidence = llmChoice.Confidence,
        Method = ClassificationMethod.LlmCloseSet,
        Rationale = llmChoice.Reasoning
    };
}
```

**Design Rationale**:
- Most files match heuristics (90%+) → fast
- Vector similarity catches variations (5-8%) → medium cost
- LLM only for ambiguous cases (2-5%) → expensive but accurate

---

### 3. **Evidence Tracking** (Citation Chain)

Every value has a provenance chain:

```
DeliverableField.SelectedValue = "$47.2M"
  ↓ Citations[0]
Passage.Text = "Total revenue for fiscal year 2023 was $47.2 million..."
  ↓ SourceFileId
SourceFile.OriginalName = "Financial_2023.pdf"
  ↓ StoragePath
/data/files/a3f5...2c8e.pdf (SHA-512 hash)
```

UI can show:
- "3 sources support this value"
- Click → evidence drawer with highlighted text
- Hover → tooltip with page number and confidence

---

### 4. **Conflict Resolution UI**

```typescript
// Frontend conflict panel
const conflicts = fields.filter(f =>
    f.status === 'Conflict' ||
    f.status === 'LowConfidence'
);

// Triage: Red (blocker) > Amber (conflict) > Yellow (low confidence)
const blocker = conflicts.filter(f => !f.selectedValue);
const conflicted = conflicts.filter(f => f.candidates.length > 1);
const lowConf = conflicts.filter(f => f.confidence < 0.7);

return (
    <ConflictsPanel>
        {blocker.map(f => <BlockerCard field={f} />)}
        {conflicted.map(f => <ConflictCard field={f} />)}
        {lowConf.map(f => <LowConfidenceCard field={f} />)}
    </ConflictsPanel>
);
```

**Each card offers**:
- View evidence
- Select alternative
- Regenerate with different hints
- Manual override with reason

---

## Testing Patterns

### Regression Coverage

- `WeaviateConnectorSpec` (`tests/Suites/Data/Connector.Weaviate/Koan.Data.Connector.Weaviate.Tests/Specs/WeaviateConnectorSpec.cs`) exercises vector CRUD, hybrid ranking, and profile binding.
    Run it with the connector suite command in Quick Start whenever the profile changes.
- `MeridianVectorWorkflowSpec` (planned for `tests/Suites/Data/S7.Meridian/Koan.Data.S7.Meridian.Tests/`) will compose the connector fixture with Meridian batching.
    That regression guards the `meridian:evidence` profile end-to-end.

### Unit Tests

```csharp
[Fact]
public async Task ExtractField_WithValidSchema_ReturnsValidatedValue()
{
    // Arrange
    var schema = JsonSchema.FromType<Revenue>();
    var passages = new[] { "Revenue: $47.2M", "Sales: $45.8M" };

    // Act
    var result = await _extractionService.ExtractFieldAsync(
        "annualRevenue", schema, passages, hints: null
    );

    // Assert
    result.Value.Should().BeOfType<decimal>();
    result.Citations.Should().NotBeEmpty();
    result.Confidence.Should().BeGreaterThan(0.7);
}

[Fact]
public async Task MergeService_WithPrecedence_SelectsFirstValid()
{
    // Arrange
    var candidates = new[] {
        new FieldCandidate { Value = 47.2M, Confidence = 0.9 },
        new FieldCandidate { Value = 45.8M, Confidence = 0.85 }
    };

    // Act
    var selected = _mergeService.ApplyMergeRule(
        candidates,
        MergeStrategy.Precedence
    );

    // Assert
    selected.Value.Should().Be(47.2M);
}
```

### Integration Tests (Testcontainers)

```csharp
public class PipelineIntegrationTests : IAsyncLifetime
{
    private MongoDbContainer _mongoContainer;
    private WeaviateContainer _weaviateContainer;

    public async Task InitializeAsync()
    {
        _mongoContainer = new MongoDbBuilder().Build();
        _weaviateContainer = new WeaviateBuilder().Build();

        await Task.WhenAll(
            _mongoContainer.StartAsync(),
            _weaviateContainer.StartAsync()
        );
    }

    [Fact]
    public async Task FullPipeline_WithSamplePDF_ProducesValidDeliverable()
    {
        // Arrange
        var request = new AnalysisRequest { /* ... */ };
        await request.Save();

        var file = await UploadTestFile("sample_financial.pdf");

        // Act
        var workflow = new AnalysisWorkflow { RequestId = request.Id };
        var result = await workflow.ExecutePipeline(CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var fields = await DeliverableField.Query(q =>
            q.Where(f => f.RequestId == request.Id));

        fields.Should().HaveCountGreaterThan(10);
        fields.All(f => f.Citations.Any()).Should().BeTrue();
    }
}
```

---

## Configuration

### appsettings.json

```json
{
  "Koan": {
    "Data": {
      "Provider": "MongoDB",
      "ConnectionString": "mongodb://localhost:27017",
      "DatabaseName": "meridian"
    },
    "Storage": {
      "DocumentsPath": "/data/files"
    }
  },
  "Meridian": {
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "EmbeddingModel": "all-minilm:latest",
      "ExtractionModel": "llama3.2:3b-instruct",
      "TypeGenerationModel": "qwen2.5:7b-instruct"
    },
    "Weaviate": {
      "Endpoint": "http://localhost:8080",
      "ClassName": "Passage"
    },
    "Processing": {
      "ChunkSize": 900,
      "ChunkOverlap": 90,
      "MaxFileSize": 52428800,
      "MaxPages": 150,
      "DefaultTopK": 12,
      "TimeoutSeconds": 15,
      "MaxRetries": 3
    }
  }
}
```

---

## Project Structure

```
S7.Meridian/
├── Models/
│   ├── SourceType.cs
│   ├── DeliverableType.cs
│   ├── AnalysisRequest.cs
│   ├── SourceFile.cs
│   ├── Passage.cs
│   ├── SourceExtraction.cs
│   ├── DeliverableField.cs
│   ├── Output.cs
│   └── ValueObjects/
│       ├── Citation.cs
│       ├── ClassificationResult.cs
│       ├── MergeRules.cs
│       └── ...
├── Controllers/
│   ├── SourceTypesController.cs
│   ├── DeliverableTypesController.cs
│   ├── AnalysisRequestsController.cs
│   ├── DeliverableFieldsController.cs
│   └── EvidenceController.cs
├── Canon/
│   ├── AnalysisWorkflow.cs
│   └── Contributors/
│       ├── IngestValidationContributor.cs
│       ├── ParseEnrichmentContributor.cs
│       ├── EmbedEnrichmentContributor.cs
│       ├── ExtractEnrichmentContributor.cs
│       ├── AggregateEnrichmentContributor.cs
│       └── RenderEnrichmentContributor.cs
├── Services/
│   ├── IDocumentStorageService.cs
│   ├── IClassificationService.cs
│   ├── IPdfParserService.cs
│   ├── IChunkingService.cs
│   ├── IEmbeddingService.cs
│   ├── IVectorSearchService.cs
│   ├── IExtractionService.cs
│   ├── IMergeService.cs
│   ├── ITemplateService.cs
│   ├── IPandocService.cs
│   └── Implementations/
│       ├── LocalDocumentStorage.cs
│       ├── CascadeClassifier.cs
│       ├── PdfPigParser.cs
│       ├── RecursiveCharacterTextSplitter.cs
│       ├── OllamaEmbeddingService.cs
│       ├── WeaviateService.cs
│       ├── OllamaExtractionService.cs
│       ├── MergeRuleEngine.cs
│       ├── MustacheRenderer.cs
│       └── PandocConverter.cs
├── Workers/
│   ├── ProcessingWorker.cs
│   └── IProcessingOrchestrator.cs
├── Initialization/
│   └── KoanAutoRegistrar.cs
├── docker/
│   └── compose.yml
├── wwwroot/         (Frontend SPA - future)
├── Program.cs
├── S7.Meridian.csproj
├── README.md        (this file)
├── ARCHITECTURE.md  (deep technical dive)
└── DESIGN.md        (UX/UI guidelines)
```

---

## Learning Outcomes

After exploring S7.Meridian, you'll understand:

1. **Entity-First Architecture**
   - Modeling complex domains with `Entity<T>`
   - Rich value objects vs primitive obsession
   - Navigation patterns between entities

2. **Canon Runtime Mastery**
   - When to use Canon (multi-stage pipelines)
   - Phase contributors (validation, enrichment)
   - Orchestration and retry logic

3. **AI Integration Patterns**
   - Schema-driven extraction (preventing hallucination)
   - Vector search for passage retrieval
   - Confidence scoring and evidence tracking
   - Local-first LLM deployment

4. **Document Intelligence Techniques**
   - Auto-classification cascades
   - Content-addressed storage
   - Citation chains for trust
   - Merge rule strategies

5. **Production Patterns**
   - Background processing (hosted services)
   - Graceful shutdown and cancellation
   - Observability and metrics
   - Error handling and recovery

---

## Related Samples

- **S5.Recs** - AI integration with Ollama + Weaviate (hybrid search pattern)
- **S6.SnapVault** - Content-addressed storage (SHA-512 pattern)
- **S8.Canon** - Canon Runtime pipelines (phase contributors)
- **S10.DevPortal** - Multi-provider demonstration (MongoDB + PostgreSQL)
- **S16.PantryPal** - Vision AI with MCP (complex AI workflows)

---

## Extension Ideas

This sample can be extended with:

- **Vision support**: Process scanned documents with llava (OCR alternative)
- **Table extraction**: Structured data from tables
- **Collaborative review**: Multi-user with role-based permissions
- **Batch processing**: Queue multiple requests
- **Export/import types**: Share source/deliverable type templates
- **Webhook notifications**: Alert when review ready
- **Custom merge rules**: User-defined resolution functions
- **Incremental refresh**: Delta detection, preserve approvals

---

## Documentation

- **README.md** (this file) - Tutorial and getting started
- **ARCHITECTURE.md** - Deep technical dive, design decisions
- **DESIGN.md** - UX/UI guidelines, component specs
- **ADRs** (future) - Architectural decision records

---

## Contributing

Found an issue or want to suggest improvements? This sample follows Koan Framework conventions:

1. **Entity<T> first**: All domain models inherit from Entity<T>
2. **EntityController<T>**: Leverage auto-generated APIs
3. **Canon for pipelines**: Multi-stage processing uses Canon
4. **Auto-registration**: Services discovered via `KoanAutoRegistrar`

See main repository CONTRIBUTING.md for details.

---

## Questions?

- **Koan Framework Docs**: https://docs.koan.dev
- **Sample Catalog**: `samples/CATALOG.md`
- **GitHub Issues**: https://github.com/koan-framework/koan/issues

---

**Maintained by**: Koan Framework Team
**Status**: Proposed (Week 1-6 implementation plan)
**Complexity**: ⭐⭐ Intermediate
**Key Capabilities**: AI integration, Canon pipelines, document intelligence, evidence tracking
