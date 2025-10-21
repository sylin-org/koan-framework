# S7.Meridian - Strategic Implementation Plan

**Document Version**: 1.0
**Created**: 2025-01-20
**Status**: Post-Carve, Ready for Phased Implementation
**Approach**: Surgical Enhancement (Option A - Infrastructure Preserved)

---

## Executive Summary

The current S7.Meridian implementation provides a **solid infrastructure foundation** (~40% complete) with production-quality job queuing, entity models, and vector indexing. The carve removed non-compliant business logic (regex-based extraction, oversimplified merge) while preserving all reusable components.

**Current State Post-Carve**:
- ✅ Durable job queue with heartbeat and retry logic
- ✅ Entity models aligned with proposal
- ✅ Text extraction (PdfPig, DOCX, plain text)
- ✅ Passage chunking and vector indexing via `VectorWorkflow<Passage>`
- ✅ Mustache template rendering for deliverables
- ❌ **REMOVED**: Regex-based field extraction (stub)
- ❌ **REMOVED**: Oversimplified merge logic (confidence-only)
- ⚠️ **TODO**: RAG-based extraction, merge policies, document classification

**Estimated Total Effort**: **18-24 working days** across 5 phases (detailed below)

---

## Phase 0: Foundation Setup (1 Day) ✅ COMPLETE

### Completed Activities
- [x] Koan.AI capability audit (found `Ai.Embed()`, `Ai.Chat()`, `Ai.Understand()`)
- [x] Configuration pattern analysis (S5.Recs, S6.SnapVault)
- [x] Embedding cache pattern harvested from S5
- [x] Code salvageability assessment
- [x] Surgical carve execution (FieldExtractor, DocumentMerger gutted)
- [x] MeridianOptions configuration infrastructure added
- [x] appsettings.json with externalized parameters

### Artifacts Created
- `Infrastructure/MeridianOptions.cs` - Externalized configuration
- `appsettings.json` - Default configuration with Koan.AI/Vector/Storage profiles
- This implementation plan

**Post-Carve Surface Area**:
```
Services/FieldExtractor.cs      - Interface + EnumerateLeafSchemas() preserved
Services/DocumentMerger.cs      - Interface + helpers preserved (BuildPayload, RenderTemplate)
All other services              - Untouched (production-ready)
All entity models               - Untouched (correctly structured)
All controllers                 - Untouched (thin wrappers)
```

---

## Phase 1: Core RAG-Based Field Extraction (5-7 Days)

**Priority**: P0 - Blocks all downstream value
**Complexity**: High
**Dependencies**: Koan.AI, VectorWorkflow, PassageIndexer

### Objective
Replace gutted `FieldExtractor` with full RAG implementation per proposal lines 1865-2300.

### Tasks

#### 1.1: Port Embedding Cache from S5.Recs (1 day)
**Files to Create**:
- `Services/IEmbeddingCache.cs` - Interface
- `Services/EmbeddingCache.cs` - File-based SHA-256 cache
- `Models/CachedEmbedding.cs` - Data model

**Implementation**:
```csharp
// Based on S5.Recs/Services/EmbeddingCache.cs
public static string ComputeContentHash(string content)
{
    var bytes = Encoding.UTF8.GetBytes(content);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

// Cache path: cache/embeddings/{entityType}/{modelId}/{contentHash}.json
```

**Acceptance Criteria**:
- [ ] Cache hit/miss logging
- [ ] SHA-256 content-addressed storage
- [ ] GetStatsAsync() for cache metrics
- [ ] FlushAsync() for cache invalidation
- [ ] Unit tests for cache operations

---

#### 1.2: Enhance PassageIndexer with Caching (0.5 day)
**File**: `Services/PassageIndexer.cs`

**Changes**:
```csharp
// Inject IEmbeddingCache
private readonly IEmbeddingCache _cache;

// Before embedding, check cache
var contentHash = EmbeddingCache.ComputeContentHash(passage.Text);
var cached = await _cache.GetAsync(contentHash, "qwen3-embedding:8b", "Passage", ct);

if (cached != null)
{
    payload.Add((passage, cached.Embedding, BuildMetadata(passage)));
    continue; // Skip AI call
}

// Cache after embedding
var embedding = await Koan.AI.Ai.Embed(passage.Text, ct);
await _cache.SetAsync(contentHash, "qwen3-embedding:8b", embedding, "Passage", ct);
```

**Acceptance Criteria**:
- [ ] Logs cache hit/miss rates
- [ ] Reuses embeddings across pipeline runs
- [ ] Integration test: re-index same document, verify cache hits

---

#### 1.3: Implement RAG Query Builder (1 day)
**File**: `Services/FieldExtractor.cs` (new private methods)

**Implementation**:
```csharp
private string BuildRAGQuery(string fieldPath, JSchema fieldSchema, DocumentPipeline pipeline)
{
    // 1. Extract field name from path: $.annualRevenue → "annualRevenue"
    var fieldName = ExtractFieldName(fieldPath);

    // 2. Apply bias notes if present
    var bias = !string.IsNullOrWhiteSpace(pipeline.BiasNotes)
        ? $" Focus on: {pipeline.BiasNotes}"
        : string.Empty;

    // 3. Build semantic query
    // TODO: Add synonym expansion from FieldSynonyms registry (future enhancement)
    return $"Find information about {fieldName}.{bias}";
}
```

**Acceptance Criteria**:
- [ ] Generates semantic queries from field paths
- [ ] Respects pipeline BiasNotes
- [ ] Unit tests for query generation

---

#### 1.4: Implement Hybrid Vector Search (1 day)
**File**: `Services/FieldExtractor.cs` (new private methods)

**Implementation**:
```csharp
private async Task<List<Passage>> RetrievePassages(
    string pipelineId,
    string query,
    MeridianOptions options,
    CancellationToken ct)
{
    // 1. Embed query
    var queryEmbedding = await Koan.AI.Ai.Embed(query, ct);

    // 2. Hybrid search via VectorWorkflow
    var results = await VectorWorkflow<Passage>.Query(
        new VectorQueryOptions(
            queryEmbedding,
            TopK: options.Retrieval.TopK,
            SearchText: query,           // Enables BM25
            Alpha: options.Retrieval.Alpha),
        profileName: MeridianConstants.VectorProfile,
        ct: ct);

    // 3. Load passages and filter by pipeline
    var passages = new List<Passage>();
    foreach (var match in results.Matches)
    {
        var passage = await Passage.Get(match.Id, ct);
        if (passage != null && passage.PipelineId == pipelineId)
        {
            passages.Add(passage);
        }
    }

    return passages;
}
```

**Acceptance Criteria**:
- [ ] Uses VectorWorkflow<Passage>.Query() correctly
- [ ] Filters by pipelineId to avoid cross-pipeline contamination
- [ ] Respects TopK and Alpha from MeridianOptions
- [ ] Integration test: verify retrieval returns relevant passages

---

#### 1.5: Implement MMR Diversity Filter (1 day)
**File**: `Services/FieldExtractor.cs` (new private methods)

**Implementation**: Per proposal lines 2047-2108 (MMR algorithm)
```csharp
private List<Passage> ApplyMMR(
    List<(Passage passage, double score, float[]? vector)> ranked,
    float[] queryEmbedding,
    int maxPassages,
    double lambda)
{
    // MMR balances relevance (query similarity) with diversity (inter-passage dissimilarity)
    // λ * relevance - (1-λ) * max_similarity_to_selected
}
```

**Acceptance Criteria**:
- [ ] Reduces redundant passages
- [ ] Configurable via MeridianOptions.Retrieval.MmrLambda
- [ ] Unit tests with mock passages

---

#### 1.6: Implement Token Budget Management (0.5 day)
**File**: `Services/FieldExtractor.cs`

**Implementation**:
```csharp
private List<Passage> EnforceTokenBudget(List<Passage> passages, int maxTokens)
{
    var estimatedTokens = 0;
    var selected = new List<Passage>();

    foreach (var passage in passages)
    {
        var passageTokens = passage.Text.Length / 4; // Rough estimate
        if (estimatedTokens + passageTokens <= maxTokens)
        {
            selected.Add(passage);
            estimatedTokens += passageTokens;
        }
        else break;
    }

    return selected.Count > 0 ? selected : passages.Take(1).ToList(); // At least 1
}
```

**Acceptance Criteria**:
- [ ] Respects MaxTokensPerField from config
- [ ] Always returns at least 1 passage
- [ ] Logs when passages are trimmed

---

#### 1.7: Implement LLM-Based Extraction (2 days)
**File**: `Services/FieldExtractor.cs`

**Implementation**: Per proposal lines 3922-3964 (deterministic prompt with hash logging)
```csharp
private async Task<ExtractionResult?> ExtractFromPassages(
    List<Passage> passages,
    string fieldPath,
    JSchema fieldSchema,
    MeridianOptions options,
    CancellationToken ct)
{
    // 1. Build prompt
    var prompt = BuildExtractionPrompt(passages, fieldPath, fieldSchema);

    // 2. Log prompt hash for reproducibility
    var promptHash = ComputePromptHash(prompt);
    _logger.LogDebug("Extraction prompt hash for {FieldPath}: {Hash}", fieldPath, promptHash);

    // 3. Call LLM with temperature control
    var response = await Koan.AI.Ai.Chat(new AiChatOptions
    {
        Message = prompt,
        Temperature = options.Extraction.Temperature,
        MaxTokens = options.Extraction.MaxOutputTokens
    }, ct);

    // 4. Parse JSON response
    var parsed = ParseExtractionResponse(response);
    if (parsed == null) return null;

    // 5. Validate against schema
    var schemaValid = ValidateAgainstSchema(parsed.Value, fieldSchema, out var validationError);

    // 6. Locate span within passage
    var bestPassage = passages[parsed.PassageIndex ?? 0];
    var span = LocateSpanInPassage(bestPassage.Text, parsed.Value);

    return new ExtractionResult
    {
        Value = parsed.Value,
        Confidence = parsed.Confidence,
        BestPassage = bestPassage,
        Span = span,
        SchemaValid = schemaValid,
        ValidationError = validationError
    };
}

private string BuildExtractionPrompt(List<Passage> passages, string fieldPath, JSchema fieldSchema)
{
    var fieldName = fieldPath.TrimStart('$', '.');
    var fieldType = fieldSchema.Type?.ToString() ?? "string";
    var schemaExcerpt = fieldSchema.ToString();

    var prompt = $@"Extract the value for '{fieldName}' from the following passages.

Field type: {fieldType}
Field schema: {schemaExcerpt}

Passages:
{string.Join("\n\n", passages.Select((p, i) => $"[{i}] {p.Text}"))}

Instructions:
1. Find the passage that best answers the question
2. Extract the EXACT value (do NOT infer or calculate)
3. If the value is not explicitly stated, respond with null
4. Validate the extracted value against the schema
5. Provide confidence based on text clarity (0.0-1.0)

Respond in JSON format:
{{
  ""value"": <extracted value matching schema type>,
  ""confidence"": <0.0-1.0>,
  ""passageIndex"": <0-based index of best passage>
}}

If the field cannot be found, respond with:
{{ ""value"": null, ""confidence"": 0.0, ""passageIndex"": null }}";

    return prompt;
}

private (string? Value, double Confidence, int? PassageIndex)? ParseExtractionResponse(string response)
{
    // Robust JSON parsing with fallback strategies (S6.SnapVault pattern)
    var json = TryParseJson(response);
    if (json == null)
    {
        // Try stripping markdown code blocks
        var cleaned = Regex.Replace(response, @"```(?:json)?\s*|\s*```", "");
        json = TryParseJson(cleaned);
    }

    if (json == null)
    {
        _logger.LogWarning("Failed to parse extraction response: {Response}", response);
        return null;
    }

    var value = json["value"]?.ToString();
    var confidence = json["confidence"]?.Value<double>() ?? 0.0;
    var passageIndex = json["passageIndex"]?.Value<int>();

    return (value, confidence, passageIndex);
}
```

**Acceptance Criteria**:
- [ ] Uses Koan.AI.Ai.Chat() with temperature control
- [ ] Logs prompt hash for reproducibility
- [ ] Parses AI responses robustly (handles markdown code blocks)
- [ ] Validates extracted values against JSON schema
- [ ] Returns null for unfound fields (graceful degradation)
- [ ] Unit tests with mocked AI responses
- [ ] Integration test: end-to-end extraction with real AI

---

#### 1.8: Implement Text Span Localization (0.5 day)
**File**: `Services/FieldExtractor.cs`

**Implementation**: Per proposal lines 2202-2299 (span localization strategies)
```csharp
private TextSpan? LocateSpanInPassage(string passageText, string extractedValue)
{
    // Strategy 1: Exact match
    var exactIndex = passageText.IndexOf(extractedValue, StringComparison.OrdinalIgnoreCase);
    if (exactIndex >= 0)
        return new TextSpan { Start = exactIndex, End = exactIndex + extractedValue.Length };

    // Strategy 2: Numeric normalization
    if (TryLocateNumeric(passageText, extractedValue, out var numericSpan))
        return numericSpan;

    // Strategy 3: Regex patterns (currency, dates, percentages)
    if (TryExtractWithRegex(passageText, extractedValue, out var regexSpan))
        return regexSpan;

    // Strategy 4: Fuzzy matching
    if (TryFuzzyLocate(passageText, extractedValue, out var fuzzySpan))
        return fuzzySpan;

    return null; // No highlighting if span not found
}
```

**Acceptance Criteria**:
- [ ] Supports numeric normalization ($47.2M → $47,200,000)
- [ ] Supports currency, date, percentage patterns
- [ ] Gracefully returns null if no span found
- [ ] Unit tests for each localization strategy

---

#### 1.9: Wire into PipelineProcessor (0.5 day)
**File**: `Services/PipelineProcessor.cs`

**Changes**:
```csharp
// Inject MeridianOptions
private readonly MeridianOptions _options;

// Pass options to field extractor
var extractions = await _fieldExtractor.ExtractAsync(pipeline, allPassages, _options, ct);
```

**Update KoanAutoRegistrar**:
```csharp
// Bind MeridianOptions from configuration
services.Configure<MeridianOptions>(cfg.GetSection("Meridian"));
services.AddSingleton(sp => sp.GetRequiredService<IOptions<MeridianOptions>>().Value);
```

**Acceptance Criteria**:
- [ ] MeridianOptions bound from appsettings.json
- [ ] Options passed through pipeline
- [ ] Integration test: full pipeline with RAG extraction

---

### Phase 1 Acceptance Criteria (End-to-End)
- [ ] Upload PDF → extract text → chunk → index → RAG extract → returns structured fields
- [ ] Cache hit rate logged (>80% on second run with same document)
- [ ] Confidence scores from AI (not hardcoded)
- [ ] Passage IDs linked to extracted values
- [ ] Schema validation errors logged
- [ ] Integration test with sample vendor RFP PDF

### Phase 1 Estimated Effort: **5-7 days**

---

## Phase 2: Merge Policies & Conflict Resolution (3-4 Days)

**Priority**: P0 - Required for multi-document pipelines
**Complexity**: Medium
**Dependencies**: Phase 1 (field extraction)

### Objective
Replace oversimplified merge logic with precedence rules, transforms, and conflict explainability per proposal lines 225-272, 2400-2550.

### Tasks

#### 2.1: Create MergePolicy Model (1 day)
**Files to Create**:
- `Models/MergePolicy.cs` - Policy configuration
- `Models/MergeStrategy.cs` - Enum for strategy types
- `Models/MergeDecision.cs` - Conflict resolution audit trail

**Implementation**:
```csharp
public class MergePolicy
{
    public string FieldPath { get; set; } = "";
    public MergeStrategy Strategy { get; set; } = MergeStrategy.HighestConfidence;

    // Precedence strategy
    public List<string> SourceTypePrecedence { get; set; } = new();

    // Transform strategy
    public string? TransformName { get; set; }

    // LatestBy strategy
    public string? LatestByFieldPath { get; set; }

    // Multi-value strategy
    public MultiValueMerge? MultiValueStrategy { get; set; }
}

public enum MergeStrategy
{
    HighestConfidence,
    Precedence,
    LatestBy,
    Consensus,
    MultiValue
}

public class MergeDecision
{
    public string FieldPath { get; set; } = "";
    public string AcceptedExtractionId { get; set; } = "";
    public List<string> RejectedExtractionIds { get; set; } = new();
    public string MergeStrategy { get; set; } = "";
    public string RuleConfig { get; set; } = ""; // JSON of policy used
    public string Explanation { get; set; } = ""; // Human-readable reason
}
```

**Acceptance Criteria**:
- [ ] Supports all merge strategies from proposal
- [ ] Serializable to/from JSON for configuration
- [ ] Unit tests for policy resolution

---

#### 2.2: Create Transform Registry (1 day)
**File**: `Services/MergeTransforms.cs`

**Implementation**:
```csharp
public static class MergeTransforms
{
    private static readonly Dictionary<string, Func<string, string>> Transforms = new()
    {
        ["normalizeToUSD"] = NormalizeToUSD,
        ["normalizeDateISO"] = NormalizeDateISO,
        ["normalizePercent"] = NormalizePercent,
        ["dedupeFuzzy"] = DedupeFuzzy, // Levenshtein distance
        ["stringToEnum"] = StringToEnum,
        ["numberRounding"] = NumberRounding
    };

    public static string? Apply(string transformName, string value)
    {
        if (!Transforms.TryGetValue(transformName, out var transform))
        {
            throw new InvalidOperationException($"Unknown transform: {transformName}");
        }
        return transform(value);
    }

    private static string NormalizeToUSD(string value)
    {
        // "$47.2M" → "47200000"
        // "€50M" → "55000000" (apply exchange rate)
    }

    private static string NormalizeDateISO(string value)
    {
        // "Oct 15, 2024" → "2024-10-15"
    }
}
```

**Acceptance Criteria**:
- [ ] Implements all transforms from proposal
- [ ] Throws on unknown transform names
- [ ] Unit tests for each transform

---

#### 2.3: Implement Merge Resolution Engine (1.5 days)
**File**: `Services/DocumentMerger.cs` (replace TODO logic)

**Implementation**:
```csharp
private ExtractedField ResolveMergeConflict(
    string fieldPath,
    IGrouping<string, ExtractedField> candidates,
    MergePolicy policy,
    out MergeDecision decision)
{
    switch (policy.Strategy)
    {
        case MergeStrategy.Precedence:
            return ResolvePrecedence(candidates, policy, out decision);

        case MergeStrategy.LatestBy:
            return ResolveLatestBy(candidates, policy, out decision);

        case MergeStrategy.Consensus:
            return ResolveConsensus(candidates, policy, out decision);

        case MergeStrategy.HighestConfidence:
        default:
            return ResolveHighestConfidence(candidates, out decision);
    }
}

private ExtractedField ResolvePrecedence(
    IGrouping<string, ExtractedField> candidates,
    MergePolicy policy,
    out MergeDecision decision)
{
    // Sort by source type precedence, then confidence, then sourceDocumentId (deterministic tie-break)
    var ranked = candidates
        .OrderBy(c => policy.SourceTypePrecedence.IndexOf(c.SourceTypeId ?? ""))
        .ThenByDescending(c => c.Confidence)
        .ThenBy(c => c.SourceDocumentId)
        .ToList();

    var accepted = ranked.First();
    var rejected = ranked.Skip(1).ToList();

    decision = new MergeDecision
    {
        FieldPath = fieldPath,
        AcceptedExtractionId = accepted.Id,
        RejectedExtractionIds = rejected.Select(r => r.Id).ToList(),
        MergeStrategy = "Precedence",
        RuleConfig = JsonConvert.SerializeObject(policy),
        Explanation = $"Applied precedence rule: {string.Join(" > ", policy.SourceTypePrecedence)}. " +
                     $"Chose {accepted.ValueJson} from {accepted.SourceTypeId} " +
                     $"(confidence: {accepted.Confidence:P0})."
    };

    foreach (var r in rejected)
    {
        r.RejectionReason = $"Lower precedence ({r.SourceTypeId}) than accepted source.";
    }

    return accepted;
}
```

**Acceptance Criteria**:
- [ ] Implements all 4 merge strategies
- [ ] Generates explainable decisions
- [ ] Deterministic tie-breaking
- [ ] Unit tests for each strategy

---

#### 2.4: Add Field Override Logic (0.5 day)
**File**: `Services/DocumentMerger.cs`

**Implementation**:
```csharp
public async Task<Deliverable> MergeAsync(DocumentPipeline pipeline, IReadOnlyList<ExtractedField> extractions, CancellationToken ct)
{
    // 1. Apply field overrides BEFORE merge
    var overridden = ApplyFieldOverrides(extractions, pipeline);

    // 2. Merge remaining fields
    var grouped = overridden.GroupBy(e => e.FieldPath, StringComparer.Ordinal);
    // ...
}

private List<ExtractedField> ApplyFieldOverrides(IReadOnlyList<ExtractedField> extractions, DocumentPipeline pipeline)
{
    var result = new List<ExtractedField>();

    foreach (var extraction in extractions)
    {
        if (extraction.Overridden)
        {
            // Use override value, skip merge
            extraction.ValueJson = extraction.OverrideValueJson;
            extraction.Confidence = 1.0; // Human override = 100% confidence
        }
        result.Add(extraction);
    }

    return result;
}
```

**Acceptance Criteria**:
- [ ] Overridden fields skip merge logic
- [ ] Override confidence set to 1.0
- [ ] Logged in RunLog with override metadata

---

#### 2.5: Add Citation Footnotes (0.5 day)
**File**: `Services/DocumentMerger.cs`

**Implementation**:
```csharp
private string AddCitationFootnotes(string markdown, IReadOnlyList<ExtractedField> acceptedFields)
{
    var footnotes = new StringBuilder();
    footnotes.AppendLine("\n\n## Citations\n");

    var citationIndex = 1;
    foreach (var field in acceptedFields)
    {
        if (!field.HasEvidenceText()) continue;

        footnotes.AppendLine($"[^{citationIndex}]: {field.Evidence.SourceDocumentId}, " +
                            $"Page {field.Evidence.Page}, " +
                            $"Section: {field.Evidence.Section ?? "N/A"}  ");
        footnotes.AppendLine($"  _\"{field.Evidence.OriginalText.Substring(0, Math.Min(100, field.Evidence.OriginalText.Length))}...\"_");
        footnotes.AppendLine();

        citationIndex++;
    }

    return markdown + footnotes.ToString();
}
```

**Acceptance Criteria**:
- [ ] Footnotes link to source documents and pages
- [ ] Excerpts show original passage text
- [ ] Markdown renders correctly
- [ ] Integration test: verify citations in deliverable

---

### Phase 2 Acceptance Criteria (End-to-End)
- [ ] Upload 3 conflicting documents → merge applies precedence rules
- [ ] Merge decisions logged with explanations
- [ ] Deliverable contains citation footnotes
- [ ] Field overrides bypass merge logic
- [ ] Integration test: vendor RFP + audit + knowledge base → merged report

### Phase 2 Estimated Effort: **3-4 days**

---

## Phase 3: Document Classification (3-4 Days)

**Priority**: P1 - Enables multi-document-type pipelines
**Complexity**: Medium
**Dependencies**: Phase 1 (AI capabilities)

### Objective
Implement heuristic → vector → LLM classification cascade per proposal lines 1406-1520.

### Tasks

#### 3.1: Create SourceType Entity (0.5 day)
**File**: `Models/SourceType.cs`

**Implementation**: Per proposal lines 467-530
```csharp
public class SourceType : Entity<SourceType>
{
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;

    // Classification hints
    public List<string> FilenamePatterns { get; set; } = new(); // Regex patterns
    public List<string> Keywords { get; set; } = new();
    public List<string> MimeTypes { get; set; } = new();
    public int? ExpectedPageCountMin { get; set; }
    public int? ExpectedPageCountMax { get; set; }

    // Schema for this document type
    public string JsonSchema { get; set; } = "{}";

    // Field query templates (optional RAG query overrides)
    public Dictionary<string, string> FieldQueries { get; set; } = new();

    // Vector embedding of typical example (for vector classification)
    public float[]? TypeEmbedding { get; set; }
    public string? TypeEmbeddingHash { get; set; } // Content hash of embedded text
}
```

**Acceptance Criteria**:
- [ ] Stores classification metadata
- [ ] Versioned for schema evolution
- [ ] Supports field-specific query templates
- [ ] CRUD API via EntityController<SourceType>

---

#### 3.2: Implement Heuristic Classifier (1 day)
**File**: `Services/DocumentClassifier.cs` (new)

**Implementation**: Per proposal lines 1433-1496
```csharp
public class DocumentClassifier : IDocumentClassifier
{
    private async Task<(string typeId, double confidence, ClassificationMethod method)?>
        TryHeuristicClassification(SourceDocument doc, CancellationToken ct)
    {
        var allTypes = await SourceType.All(ct);

        foreach (var type in allTypes)
        {
            var score = 0.0;
            var maxScore = 0.0;

            // Check filename patterns
            if (type.FilenamePatterns.Count > 0)
            {
                maxScore += 0.3;
                foreach (var pattern in type.FilenamePatterns)
                {
                    if (Regex.IsMatch(doc.OriginalFileName, pattern, RegexOptions.IgnoreCase))
                    {
                        score += 0.3;
                        break;
                    }
                }
            }

            // Check keywords presence in extracted text
            if (type.Keywords.Count > 0)
            {
                maxScore += 0.3;
                var extractedText = await GetExtractedText(doc, ct);
                var matchedKeywords = type.Keywords.Count(kw =>
                    extractedText.Contains(kw, StringComparison.OrdinalIgnoreCase));
                score += 0.3 * (matchedKeywords / (double)type.Keywords.Count);
            }

            // Check page count range
            if (type.ExpectedPageCountMin.HasValue || type.ExpectedPageCountMax.HasValue)
            {
                maxScore += 0.2;
                var inRange = (!type.ExpectedPageCountMin.HasValue || doc.PageCount >= type.ExpectedPageCountMin.Value) &&
                              (!type.ExpectedPageCountMax.HasValue || doc.PageCount <= type.ExpectedPageCountMax.Value);
                if (inRange) score += 0.2;
            }

            // Check MIME type
            if (type.MimeTypes.Count > 0)
            {
                maxScore += 0.2;
                if (type.MimeTypes.Contains(doc.MediaType))
                    score += 0.2;
            }

            if (maxScore > 0)
            {
                var confidence = score / maxScore;
                if (confidence > 0.9)
                    return (type.Id, confidence, ClassificationMethod.Heuristic);
            }
        }

        return null; // No high-confidence heuristic match
    }
}
```

**Acceptance Criteria**:
- [ ] Supports filename patterns, keywords, page ranges, MIME types
- [ ] Returns confidence >0.9 for heuristic match
- [ ] Falls through if confidence too low
- [ ] Unit tests with sample documents

---

#### 3.3: Implement Vector Classifier (1 day)
**File**: `Services/DocumentClassifier.cs`

**Implementation**: Per proposal lines 1498-1550
```csharp
private async Task<(string typeId, double confidence, ClassificationMethod method)?>
    TryVectorClassification(SourceDocument doc, CancellationToken ct)
{
    // 1. Embed first 1000 chars of document
    var preview = doc.ExtractedText.Length > 1000
        ? doc.ExtractedText.Substring(0, 1000)
        : doc.ExtractedText;

    var docEmbedding = await Koan.AI.Ai.Embed(preview, ct);

    // 2. Compare to SourceType embeddings
    var allTypes = await SourceType.All(ct);
    var bestMatch = allTypes
        .Where(t => t.TypeEmbedding != null)
        .Select(t => (
            typeId: t.Id,
            similarity: CosineSimilarity(docEmbedding, t.TypeEmbedding!)
        ))
        .OrderByDescending(m => m.similarity)
        .FirstOrDefault();

    if (bestMatch.similarity > 0.75)
    {
        return (bestMatch.typeId, bestMatch.similarity, ClassificationMethod.Vector);
    }

    return null; // No high-confidence vector match
}

private double CosineSimilarity(float[] a, float[] b)
{
    if (a.Length != b.Length) return 0.0;

    double dot = 0.0, magA = 0.0, magB = 0.0;
    for (int i = 0; i < a.Length; i++)
    {
        dot += a[i] * b[i];
        magA += a[i] * a[i];
        magB += b[i] * b[i];
    }

    return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
}
```

**Acceptance Criteria**:
- [ ] Embeds document preview
- [ ] Compares to SourceType embeddings
- [ ] Returns confidence >0.75 for vector match
- [ ] Unit tests with mock embeddings

---

#### 3.4: Implement LLM Classifier (Fallback) (1 day)
**File**: `Services/DocumentClassifier.cs`

**Implementation**:
```csharp
private async Task<(string typeId, double confidence, ClassificationMethod method)>
    LLMClassification(SourceDocument doc, CancellationToken ct)
{
    var allTypes = await SourceType.All(ct);
    var typeDescriptions = string.Join("\n", allTypes.Select(t =>
        $"- {t.Name}: {t.Keywords.FirstOrDefault() ?? "General document"}"));

    var prompt = $@"Classify this document into one of the following types:

{typeDescriptions}

Document preview (first 1000 chars):
{doc.ExtractedText.Substring(0, Math.Min(1000, doc.ExtractedText.Length))}

Respond in JSON:
{{
  ""typeId"": ""<type name>"",
  ""confidence"": <0.0-1.0>
}}";

    var response = await Koan.AI.Ai.Chat(prompt, ct);
    var parsed = ParseClassificationResponse(response);

    if (parsed == null)
    {
        // Fallback: return first type with low confidence
        return (allTypes.First().Id, 0.3, ClassificationMethod.LLM);
    }

    return (parsed.TypeId, parsed.Confidence, ClassificationMethod.LLM);
}
```

**Acceptance Criteria**:
- [ ] Uses LLM as final fallback
- [ ] Returns classification with confidence
- [ ] Handles parse failures gracefully
- [ ] Integration test: real LLM classification

---

#### 3.5: Wire Classification into Pipeline (0.5 day)
**File**: `Services/PipelineProcessor.cs`

**Changes**:
```csharp
// After text extraction, before chunking
var classifier = _sp.GetRequiredService<IDocumentClassifier>();
var (typeId, confidence, method) = await classifier.ClassifyAsync(document, ct);

document.ClassifiedTypeId = typeId;
document.ClassifiedTypeVersion = (await SourceType.Get(typeId, ct))?.Version;
document.ClassificationConfidence = confidence;
document.ClassificationMethod = method;
await document.Save(ct);
```

**Acceptance Criteria**:
- [ ] Classifies documents before field extraction
- [ ] Stores classification metadata
- [ ] Logs classification method used
- [ ] Integration test: upload document, verify classification

---

### Phase 3 Acceptance Criteria (End-to-End)
- [ ] Create 3 SourceTypes with different schemas
- [ ] Upload documents matching each type
- [ ] Heuristic classification succeeds for obvious matches
- [ ] Vector/LLM classification succeeds for ambiguous cases
- [ ] Field extraction uses type-specific schemas
- [ ] Integration test: multi-type pipeline

### Phase 3 Estimated Effort: **3-4 days**

---

## Phase 4: Production Features (3-5 Days)

**Priority**: P1-P2 - Improves UX and performance
**Complexity**: Low-Medium
**Dependencies**: Phases 1-2

### Tasks

#### 4.1: Field Override API (1 day)
**File**: `Controllers/FieldOverridesController.cs` (new)

**Implementation**:
```csharp
[ApiController]
[Route("api/pipelines/{pipelineId}/fields")]
public class FieldOverridesController : ControllerBase
{
    [HttpPost("{fieldPath}/override")]
    public async Task<IActionResult> SetOverride(
        string pipelineId,
        string fieldPath,
        [FromBody] FieldOverrideRequest request,
        CancellationToken ct)
    {
        var extraction = await ExtractedField.Query(
            e => e.PipelineId == pipelineId && e.FieldPath == fieldPath, ct)
            .FirstOrDefaultAsync(ct);

        if (extraction == null) return NotFound();

        extraction.Overridden = true;
        extraction.OverrideValueJson = JsonConvert.SerializeObject(request.Value);
        extraction.OverrideReason = request.Reason;
        extraction.OverriddenBy = request.User;
        extraction.OverriddenAt = DateTime.UtcNow;
        await extraction.Save(ct);

        return Ok(extraction);
    }

    [HttpDelete("{fieldPath}/override")]
    public async Task<IActionResult> ClearOverride(
        string pipelineId,
        string fieldPath,
        CancellationToken ct)
    {
        var extraction = await ExtractedField.Query(
            e => e.PipelineId == pipelineId && e.FieldPath == fieldPath, ct)
            .FirstOrDefaultAsync(ct);

        if (extraction == null) return NotFound();

        extraction.Overridden = false;
        extraction.OverrideValueJson = null;
        extraction.OverrideReason = null;
        extraction.OverriddenBy = null;
        extraction.OverriddenAt = null;
        await extraction.Save(ct);

        return Ok(extraction);
    }
}
```

**Acceptance Criteria**:
- [ ] POST creates override
- [ ] DELETE clears override
- [ ] Requires justification reason
- [ ] Integration test: override field, verify merge uses override

---

#### 4.2: Incremental Refresh (2 days)
**File**: `Services/RefreshService.cs` (new)

**Implementation**: Per proposal lines 187-223
```csharp
public async Task RefreshAnalysisAsync(string pipelineId, CancellationToken ct)
{
    // 1. Find new/changed documents
    var allDocs = await SourceDocument.Query(d => d.PipelineId == pipelineId, ct);
    var previousRun = await GetLastSuccessfulRun(pipelineId, ct);

    var changedDocs = allDocs
        .Where(d => d.UploadedAt > previousRun?.CompletedAt)
        .ToList();

    if (changedDocs.Count == 0)
    {
        _logger.LogInformation("No changed documents for pipeline {PipelineId}", pipelineId);
        return;
    }

    // 2. Calculate impacted fields (all fields from changed docs)
    var impactedFields = await CalculateImpactedFields(pipelineId, changedDocs, ct);

    // 3. Re-extract impacted fields
    foreach (var fieldPath in impactedFields)
    {
        var oldExtraction = await GetCurrentExtraction(pipelineId, fieldPath, ct);
        var newExtraction = await ReExtractField(pipelineId, fieldPath, ct);

        // 4. Preserve approval if evidence unchanged
        if (oldExtraction != null && oldExtraction.UserApproved)
        {
            if (EvidenceUnchanged(oldExtraction.Evidence, newExtraction.Evidence))
            {
                newExtraction.UserApproved = true;
                newExtraction.ApprovedBy = oldExtraction.ApprovedBy;
                newExtraction.ApprovedAt = oldExtraction.ApprovedAt;
            }
        }

        await newExtraction.Save(ct);
    }

    // 5. Re-merge and render
    var allExtractions = await ExtractedField.Query(e => e.PipelineId == pipelineId, ct);
    await _merger.MergeAsync(pipeline, allExtractions, ct);
}

private bool EvidenceUnchanged(TextSpanEvidence old, TextSpanEvidence @new)
{
    // Normalize values for comparison (currency, dates, etc.)
    var oldNormalized = NormalizeValue(old.OriginalText);
    var newNormalized = NormalizeValue(@new.OriginalText);
    return oldNormalized == newNormalized;
}
```

**Acceptance Criteria**:
- [ ] Detects changed documents since last run
- [ ] Re-extracts only impacted fields
- [ ] Preserves approvals when evidence unchanged
- [ ] Integration test: upload new doc, verify incremental refresh

---

#### 4.3: Page Number Extraction (0.5 day)
**File**: `Services/PassageChunker.cs`

**Enhancement**:
```csharp
// Use PdfPig's page.Number instead of estimation
foreach (var page in pdf.GetPages())
{
    var pageText = page.Text;
    var paragraphs = pageText.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

    foreach (var para in paragraphs)
    {
        passages.Add(new Passage
        {
            // ...
            PageNumber = page.Number, // Real page number from PDF
            // ...
        });
    }
}
```

**Acceptance Criteria**:
- [ ] Uses PdfPig page numbers
- [ ] Accurate for all PDF types
- [ ] Integration test: verify correct page numbers

---

#### 4.4: Sample Test Data (1 day)
**Folder**: `samples/S7.Meridian/TestData/`

**Download/Create**:
- `VendorRFP.pdf` - Sample RFP with financial data
- `AuditReport.pdf` - Sample audit with different financial data
- `TechnicalSpecs.pdf` - Sample technical documentation
- `TestPipeline.json` - Sample pipeline configuration with schema

**Acceptance Criteria**:
- [ ] PDFs are non-copyrighted, safe for testing
- [ ] Cover different document types
- [ ] Test pipeline produces working deliverable
- [ ] Integration test uses test data

---

#### 4.5: Unit and Integration Tests (1 day)
**Folders**: `tests/S7.Meridian.Tests/`

**Coverage**:
- Unit tests for all new services
- Integration tests for end-to-end flows
- Mock AI responses for deterministic tests
- Real AI tests (marked `[Fact(Skip = "Requires AI")]`)

**Minimum Coverage**:
- [ ] EmbeddingCache operations
- [ ] RAG query building
- [ ] MMR diversity filter
- [ ] Merge policy resolution
- [ ] Transform registry
- [ ] Document classification cascade
- [ ] End-to-end pipeline: upload → extract → merge → render

---

### Phase 4 Acceptance Criteria (End-to-End)
- [ ] Override API works
- [ ] Incremental refresh preserves approvals
- [ ] Page numbers accurate
- [ ] Test data validates full pipeline
- [ ] Unit tests pass
- [ ] Integration tests pass

### Phase 4 Estimated Effort: **3-5 days**

---

## Phase 5: Production Hardening (2-4 Days)

**Priority**: P2 - Security and robustness
**Complexity**: Medium
**Dependencies**: All previous phases

### Tasks

#### 5.1: Prompt Injection Defense (0.5 day)
**File**: `Services/FieldExtractor.cs`

**Enhancement**:
```csharp
private string SanitizePassage(string passageText)
{
    // Remove potential injection patterns
    var sanitized = passageText
        .Replace("Ignore previous instructions", "[SANITIZED]")
        .Replace("Disregard all prior", "[SANITIZED]")
        .Replace("You are now", "[SANITIZED]");

    return sanitized;
}

// Update BuildExtractionPrompt
var sanitizedPassages = passages.Select((p, i) => $"[{i}] {SanitizePassage(p.Text)}");
```

**Acceptance Criteria**:
- [ ] Blocks common injection patterns
- [ ] Logged when sanitization occurs
- [ ] Unit tests with adversarial passages

---

#### 5.2: Template Sandboxing (1 day)
**File**: `Services/DocumentMerger.cs`

**Enhancement**:
```csharp
private string SanitizeMarkdown(string markdown)
{
    // Block LaTeX shell-escape patterns
    var sanitized = markdown
        .Replace("\\input", "[BLOCKED]")
        .Replace("\\include", "[BLOCKED]")
        .Replace("\\write18", "[BLOCKED]");

    return sanitized;
}

// Before rendering
markdown = SanitizeMarkdown(markdown);

// TODO: If adding Pandoc PDF rendering:
// - Restrict templates to read-only /app/templates/ directory
// - Use --no-shell-escape flag in texmf config
// - Cache PDFs by (dataHash, templateHash) for reproducibility
```

**Acceptance Criteria**:
- [ ] Blocks shell-escape patterns
- [ ] Templates loaded from trusted directory only
- [ ] Unit tests with malicious templates

---

#### 5.3: Numeric Type Enforcement (0.5 day)
**File**: `Services/FieldExtractor.cs`

**Enhancement**:
```csharp
private (string? ValueJson, bool SchemaValid, string? ValidationError) ParseAndValidate(
    string aiResponse,
    JSchema fieldSchema)
{
    var parsed = JToken.Parse(aiResponse);

    // Attempt type repair
    if (fieldSchema.Type == JSchemaType.Number && parsed.Type == JTokenType.String)
    {
        if (double.TryParse(parsed.Value<string>(), out var numeric))
        {
            parsed = new JValue(numeric); // Repair: string → number
        }
    }

    // Validate against schema
    if (!parsed.IsValid(fieldSchema, out IList<string> errors))
    {
        return (null, false, string.Join("; ", errors));
    }

    return (parsed.ToString(), true, null);
}
```

**Acceptance Criteria**:
- [ ] Repairs string-to-number mismatches
- [ ] Validates all extracted values
- [ ] Logs validation errors
- [ ] Unit tests for type repair

---

#### 5.4: Classification Metadata Cache (1 day)
**File**: `Services/DocumentClassifier.cs`

**Enhancement**:
```csharp
public class DocumentClassifier
{
    private static CachedTypeMetadata? _cache;
    private static DateTime _cacheInvalidatedAt;

    private async Task<CachedTypeMetadata> GetTypeMetadata(CancellationToken ct)
    {
        var latestUpdate = (await SourceType.All(ct))
            .Max(t => t.UpdatedAt);

        if (_cache == null || latestUpdate > _cacheInvalidatedAt)
        {
            _cache = new CachedTypeMetadata
            {
                Types = await SourceType.All(ct),
                CachedAt = DateTime.UtcNow
            };
            _cacheInvalidatedAt = latestUpdate;
            _logger.LogInformation("Classification metadata cache refreshed");
        }

        return _cache;
    }
}
```

**Acceptance Criteria**:
- [ ] Caches SourceType metadata in memory
- [ ] Invalidates on UpdatedAt changes
- [ ] Reduces O(N) queries to O(1)
- [ ] Unit tests for cache invalidation

---

#### 5.5: Retry and Timeout Policies (1 day)
**File**: `Services/FieldExtractor.cs`, `Services/DocumentClassifier.cs`

**Enhancement**:
```csharp
// Use Polly for retry policies
private async Task<string> CallAIWithRetry(string prompt, CancellationToken ct)
{
    var retryPolicy = Policy
        .Handle<Exception>()
        .WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

    return await retryPolicy.ExecuteAsync(async () =>
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60)); // 60s timeout

        return await Koan.AI.Ai.Chat(prompt, cts.Token);
    });
}
```

**Acceptance Criteria**:
- [ ] 3 retries with exponential backoff
- [ ] 60s timeout per AI call
- [ ] Logs retries and failures
- [ ] Integration test with flaky AI responses

---

#### 5.6: Observability and Metrics (0.5 day)
**File**: `Services/PipelineProcessor.cs`

**Enhancement**:
```csharp
// Add structured logging
_logger.LogInformation(
    "Pipeline {PipelineId} processing complete: {Duration}ms, {FieldCount} fields, {Quality}% coverage",
    pipeline.Id,
    sw.ElapsedMilliseconds,
    extractions.Count,
    pipeline.Quality.CitationCoverage);

// Add telemetry (future: OpenTelemetry)
// Activity.Current?.SetTag("pipeline.id", pipeline.Id);
// Activity.Current?.SetTag("pipeline.field_count", extractions.Count);
```

**Acceptance Criteria**:
- [ ] Structured logs for key events
- [ ] Duration tracking for stages
- [ ] Quality metrics logged
- [ ] Ready for OpenTelemetry integration

---

### Phase 5 Acceptance Criteria (End-to-End)
- [ ] Prompt injection blocked
- [ ] Template sandboxing prevents shell-escape
- [ ] Numeric types enforced
- [ ] Classification cache reduces queries
- [ ] Retries handle transient failures
- [ ] Observability ready

### Phase 5 Estimated Effort: **2-4 days**

---

## Total Effort Summary

| Phase | Priority | Effort (Days) | Dependencies | Status |
|-------|----------|---------------|--------------|--------|
| **Phase 0: Foundation Setup** | P0 | 1 | None | ✅ COMPLETE |
| **Phase 1: RAG Extraction** | P0 | 5-7 | Phase 0 | ⏳ NEXT |
| **Phase 2: Merge Policies** | P0 | 3-4 | Phase 1 | ⏳ PENDING |
| **Phase 3: Classification** | P1 | 3-4 | Phase 1 | ⏳ PENDING |
| **Phase 4: Production Features** | P1-P2 | 3-5 | Phases 1-2 | ⏳ PENDING |
| **Phase 5: Production Hardening** | P2 | 2-4 | All | ⏳ PENDING |
| **TOTAL** | - | **18-26 days** | - | **4% COMPLETE** |

---

## Risk Assessment

### High Risks
1. **AI Hallucinations** - LLM may extract incorrect values
   - **Mitigation**: Schema validation, confidence thresholds, human review flags

2. **Vector Search Irrelevance** - RAG may retrieve wrong passages
   - **Mitigation**: MMR diversity, token budget limits, Top-K tuning

3. **Performance with Large Documents** - 100-page PDFs may timeout
   - **Mitigation**: Parallel extraction, caching, pagination

### Medium Risks
1. **Schema Evolution** - Changing SourceType schemas breaks pipelines
   - **Mitigation**: Version pinning (already in model)

2. **Merge Policy Complexity** - Users may not understand precedence rules
   - **Mitigation**: Explainability UI, merge decision audit trail

### Low Risks
1. **Storage Costs** - Embedding cache grows unbounded
   - **Mitigation**: FlushAsync() API, TTL policies (future)

---

## Success Criteria (MVP Definition)

**Phase 1-2 Complete = Minimum Viable Product**

The system can:
- [x] Upload PDF documents
- [x] Extract text via PdfPig
- [x] Chunk into passages
- [x] Index passages via VectorWorkflow
- [ ] **Extract structured fields via RAG** (Phase 1)
- [ ] **Merge conflicting values via precedence** (Phase 2)
- [ ] Render Markdown deliverable with citations
- [ ] Provide field override API

**Post-MVP Enhancements** (Phases 3-5):
- Multi-document-type pipelines (classification)
- Incremental refresh
- Production hardening

---

## Next Steps

### Immediate Actions (You)
1. **Review this plan** - Approve, adjust priorities, or request changes
2. **Clarify unknowns** - Any questions on Koan.AI usage, vector workflow, or proposal interpretation?
3. **Authorize Phase 1 start** - I'm ready to implement when you give the green light

### Implementation Approach (Me)
1. **Incremental updates** - I'll complete each task and report back before moving on
2. **Working code** - Every commit will compile and pass existing tests
3. **No placeholders** - All implementations will be production-grade, no TODOs except where explicitly noted
4. **Test coverage** - Unit + integration tests for each component

**Ready to proceed with Phase 1: Core RAG-Based Field Extraction?**

---

## Appendix: Key Code Artifacts

### Preserved Infrastructure
- `Models/ProcessingJob.cs` - Durable queue ✅
- `Services/MeridianJobWorker.cs` - Background worker ✅
- `Services/TextExtractor.cs` - PDF/DOCX extraction ✅
- `Services/PassageChunker.cs` - Semantic chunking ✅
- `Services/PassageIndexer.cs` - Vector workflow ✅
- `Services/DocumentStorage.cs` - Storage abstraction ✅
- `Services/DocumentIngestionService.cs` - Upload flow ✅
- `Services/JobCoordinator.cs` - Job scheduling ✅
- `Services/RunLogWriter.cs` - Audit trail ✅
- All entity models - Correctly structured ✅
- All controllers - Koan patterns ✅

### Carved (Awaiting Implementation)
- `Services/FieldExtractor.cs` - Gutted, spec documented ⏳
- `Services/DocumentMerger.cs` - Simplified, TODO markers ⏳

### New Artifacts Created
- `Infrastructure/MeridianOptions.cs` - Configuration ✅
- `appsettings.json` - Default config ✅
- `IMPLEMENTATION_PLAN.md` (this document) ✅

### To Be Created (Phases 1-5)
- `Services/IEmbeddingCache.cs` + `EmbeddingCache.cs`
- `Models/CachedEmbedding.cs`
- `Models/MergePolicy.cs` + `MergeDecision.cs`
- `Services/MergeTransforms.cs`
- `Models/SourceType.cs`
- `Services/DocumentClassifier.cs`
- `Services/RefreshService.cs`
- `Controllers/FieldOverridesController.cs`
- `tests/S7.Meridian.Tests/**`
- `TestData/**`

---

**Document Ends** - Awaiting your approval to proceed with Phase 1.
