# MERIDIAN-PLAN: Daily Resume Document

**Purpose**: Complete implementation reference for S7.Meridian evidence-backed narrative generation system.
**Usage**: Point Claude to this document daily with: _"Implement the plan delineated in MERIDIAN-PLAN.md, resume from current checkpoint"_

---

## 📊 CURRENT STATUS SNAPSHOT

**Last Updated**: 2025-10-21
**Phase**: Phase 1 - Core RAG-Based Field Extraction
**Progress**: 10/10 primary tasks complete (Phase 1 ready for review)
**Current Task**: Transition to Phase 2 - Merge Policy Enhancements
**Next Checkpoint**: Sign-off on end-to-end harness + Phase 2 kickoff

### Phase Completion Status
```
✅ Phase 0: Foundation Setup (COMPLETE)
✅ Phase 1: Core RAG Extraction (COMPLETE)
⬜ Phase 2: Merge Policies (PENDING)
⬜ Phase 3: Document Classification (PENDING)
⬜ Phase 4: Production Features (PENDING)
⬜ Phase 5: Production Hardening (PENDING)
```

### What Works Right Now
```bash
✅ Upload documents via `/api/pipelines/{id}/documents`
✅ Text extraction (PDF, DOCX, plain text) + semantic chunking
✅ Vector indexing with on-disk embedding cache (SHA-256 keys)
✅ Hybrid RAG retrieval (BM25 + embeddings) with MMR diversity
✅ Token-budgeted LLM extraction w/ schema validation + span mapping
✅ Durable job orchestration + background worker heartbeat
✅ Markdown deliverable rendering with evidence metadata
✅ Automated end-to-end validation harness (Task 1.E2E)
```

### What Doesn't Work Yet
```bash
⚠️ Merge policy enhancements (Phase 2 scope)
❌ Document classification pipeline
❌ Field overrides + incremental refresh
```


---

## 🎯 IMPLEMENTATION CONFIGURATION

### AI Models (Configured)
- **Text/Chat**: `granite3.3:8b` (for field extraction)
- **Vision**: `qwen2.5vl` (future - not used in Phase 1)
- **Embeddings**: `granite3.3:8b` (reuse chat model)

### Target Metrics
- **Accuracy**: Primary goal (speed secondary)
- **Cache Hit Rate**: >80% on second run
- **Confidence Scores**: From AI (not hardcoded)

### Configuration Files
- `appsettings.json`: MeridianOptions bound from `Meridian` section
- `Infrastructure/MeridianOptions.cs`: Externalized parameters
- `Infrastructure/MeridianConstants.cs`: Profile names, routes

---

## 📁 PROJECT STRUCTURE (Post-Carve)

```
samples/S7.Meridian/
├── Controllers/
│   ├── PipelinesController.cs       ✅ EntityController<DocumentPipeline>
│   ├── DocumentsController.cs       ✅ Upload endpoint
│   ├── JobsController.cs            ✅ Job status
│   └── DeliverablesController.cs    ✅ Latest deliverable
├── Models/
│   ├── DocumentPipeline.cs          ✅ With Quality metrics
│   ├── SourceDocument.cs            ✅ With classification fields
│   ├── Passage.cs                   ✅ Chunked text
│   ├── ExtractedField.cs            ✅ With evidence + overrides
│   ├── Deliverable.cs               ✅ Markdown output
│   ├── ProcessingJob.cs             ✅ Durable queue
│   └── RunLog.cs                    ✅ Audit trail
├── Services/
│   ├── FieldExtractor.cs            ✅ Full RAG pipeline (Phase 1 delivered)
│   ├── DocumentMerger.cs            ⚠️ SIMPLIFIED - Enhance in Phase 2
│   ├── TextExtractor.cs             ✅ PdfPig + DOCX
│   ├── PassageChunker.cs            ✅ Semantic chunking
│   ├── PassageIndexer.cs            ✅ Embedding cache integration complete
│   ├── DocumentStorage.cs           ✅ Storage abstraction
│   ├── DocumentIngestionService.cs  ✅ Upload flow
│   ├── JobCoordinator.cs            ✅ Job scheduling
│   ├── PipelineProcessor.cs         ✅ Orchestration
│   ├── RunLogWriter.cs              ✅ Audit logging
│   ├── PipelineAlerts.cs            ✅ Warnings
│   └── MeridianJobWorker.cs         ✅ Background service
├── Infrastructure/
│   ├── MeridianConstants.cs         ✅ Constants
│   └── MeridianOptions.cs           ✅ Configuration
├── Initialization/
│   └── KoanAutoRegistrar.cs         ✅ DI registration
├── appsettings.json                 ✅ Configuration
├── PROPOSAL.md                      📖 Original spec (47k tokens)
├── IMPLEMENTATION_PLAN.md           📋 26-day roadmap
├── PHASE_1_TODOS.md                 ✅ 53 detailed checkboxes
└── MERIDIAN-PLAN.md                 📍 THIS FILE
```

---

## 🔄 RESUME FROM HERE: Phase 1 Detailed Plan

### Phase 1 Overview
**Objective**: Replace gutted FieldExtractor with full RAG implementation
**Effort**: 5-7 days
**Tasks**: 9 main tasks, 53 checkboxes
**End Goal**: Upload PDF → AI extracts structured fields with confidence scores

### Phase 1 Task Breakdown

#### ✅ SETUP (Before Implementation)
**☐ SETUP-1: Verify Ollama Configuration**
```bash
# Commands to run:
ollama list | grep granite3.3
ollama run granite3.3:8b "What is 2+2?"

# Test from code:
var embedding = await Koan.AI.Ai.Embed("test", ct);
var chat = await Koan.AI.Ai.Chat("hello", ct);
```

**Acceptance**: Ollama responds successfully, models available

---

#### ⬜ Task 1.1: Port Embedding Cache from S5.Recs (1 day)

**Files to Create**:
1. `Services/IEmbeddingCache.cs`
2. `Models/CachedEmbedding.cs`
3. `Services/EmbeddingCache.cs`

**Reference Pattern** (from S5.Recs):
```csharp
// Cache path structure: cache/embeddings/{entityType}/{modelId}/{hash}.json
public static string ComputeContentHash(string content)
{
    var bytes = Encoding.UTF8.GetBytes(content);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

// Cache model
public sealed class CachedEmbedding
{
    public required string ContentHash { get; init; }
    public required string ModelId { get; init; }
    public required float[] Embedding { get; init; }
    public required int Dimension { get; init; }
    public required DateTimeOffset CachedAt { get; init; }
}

// Interface
public interface IEmbeddingCache
{
    Task<CachedEmbedding?> GetAsync(string contentHash, string modelId, string entityTypeName, CancellationToken ct = default);
    Task SetAsync(string contentHash, string modelId, float[] embedding, string entityTypeName, CancellationToken ct = default);
    Task<int> FlushAsync(CancellationToken ct = default);
    Task<CacheStats> GetStatsAsync(CancellationToken ct = default);
}
```

**Subtasks**:
- ☐ TODO-1.1.1: Create IEmbeddingCache Interface
- ☐ TODO-1.1.2: Create CachedEmbedding Model
- ☐ TODO-1.1.3: Implement EmbeddingCache Service (file-based, SHA-256 keys)
- ☐ TODO-1.1.4: Register in DI (`KoanAutoRegistrar.cs`)

**Test**:
```csharp
var cache = new EmbeddingCache(logger);
var hash = EmbeddingCache.ComputeContentHash("test content");
await cache.SetAsync(hash, "granite3.3:8b", new float[] { 0.1f, 0.2f }, "Passage", ct);
var cached = await cache.GetAsync(hash, "granite3.3:8b", "Passage", ct);
Assert.NotNull(cached);
Assert.Equal(2, cached.Embedding.Length);
```

---

#### ⬜ Task 1.2: Enhance PassageIndexer with Caching (0.5 day)

**File to Modify**: `Services/PassageIndexer.cs`

**Implementation Pattern**:
```csharp
public sealed class PassageIndexer : IPassageIndexer
{
    private readonly IEmbeddingCache _cache;
    private readonly ILogger<PassageIndexer> _logger;
    private readonly IPipelineAlertService _alerts;

    public async Task IndexAsync(List<Passage> passages, CancellationToken ct)
    {
        if (passages.Count == 0) return;

        if (!VectorWorkflow<Passage>.IsAvailable(MeridianConstants.VectorProfile))
        {
            _logger.LogWarning("Vector workflow unavailable; skipping indexing.");
            return;
        }

        await VectorWorkflow<Passage>.EnsureCreated(MeridianConstants.VectorProfile, ct);

        var hits = 0;
        var misses = 0;
        var payload = new List<(Passage Entity, float[] Embedding, object? Metadata)>();

        foreach (var passage in passages)
        {
            // Check cache
            var contentHash = EmbeddingCache.ComputeContentHash(passage.Text);
            var cached = await _cache.GetAsync(contentHash, "granite3.3:8b", "Passage", ct);

            if (cached != null)
            {
                hits++;
                _logger.LogDebug("Embedding cache HIT for passage {PassageId}", passage.Id);
                payload.Add((passage, cached.Embedding, BuildMetadata(passage)));
            }
            else
            {
                misses++;
                _logger.LogDebug("Embedding cache MISS for passage {PassageId}", passage.Id);
                var embedding = await Koan.AI.Ai.Embed(passage.Text, ct);
                await _cache.SetAsync(contentHash, "granite3.3:8b", embedding, "Passage", ct);
                payload.Add((passage, embedding, BuildMetadata(passage)));
            }

            passage.IndexedAt = DateTime.UtcNow;
            await passage.Save(ct);
        }

        _logger.LogInformation("Embedding cache: {Hits} hits, {Misses} misses ({Total} total)",
            hits, misses, passages.Count);

        if (payload.Count > 0)
        {
            var result = await VectorWorkflow<Passage>.SaveMany(payload, MeridianConstants.VectorProfile, ct);
            _logger.LogInformation("Upserted {Count} passages into vector profile {Profile}.",
                result.Documents, MeridianConstants.VectorProfile);
        }
    }
}
```

**Subtasks**:
- ☐ TODO-1.2.1: Inject IEmbeddingCache into PassageIndexer
- ☐ TODO-1.2.2: Implement cache-aware embedding logic with hit/miss logging

**Test**: Re-index same passages, verify cache hits

---

#### ⬜ Task 1.3: Implement RAG Query Builder (1 day)

**File to Modify**: `Services/FieldExtractor.cs`

**Method to Add**:
```csharp
private string BuildRAGQuery(string fieldPath, JSchema fieldSchema, DocumentPipeline pipeline)
{
    // Extract field name: $.annualRevenue → "annual revenue"
    var fieldName = fieldPath.TrimStart('$', '.');

    // Convert camelCase to spaced: annualRevenue → annual revenue
    var spaced = Regex.Replace(fieldName, "([a-z])([A-Z])", "$1 $2").ToLower();

    // Apply bias if present
    var bias = !string.IsNullOrWhiteSpace(pipeline.BiasNotes)
        ? $" {pipeline.BiasNotes}"
        : string.Empty;

    return $"Find information about {spaced}.{bias}";
}
```

**Subtasks**:
- ☐ TODO-1.3.1: Add BuildRAGQuery method with camelCase conversion

**Test**:
```csharp
var query = BuildRAGQuery("$.annualRevenue", schema, pipeline);
Assert.Equal("Find information about annual revenue.", query);
```

---

#### ⬜ Task 1.4: Implement Hybrid Vector Search (1 day)

**File to Modify**: `Services/FieldExtractor.cs`

**Method to Add** (Per Proposal Lines 2003-2045):
```csharp
private async Task<List<Passage>> RetrievePassages(
    string pipelineId,
    string query,
    MeridianOptions options,
    CancellationToken ct)
{
    // 1. Embed query
    _logger.LogDebug("Embedding query: {Query}", query);
    var queryEmbedding = await Koan.AI.Ai.Embed(query, ct);

    // 2. Hybrid search via VectorWorkflow
    var results = await VectorWorkflow<Passage>.Query(
        new VectorQueryOptions(
            queryEmbedding,
            TopK: options.Retrieval.TopK,
            SearchText: query,           // Enables BM25 hybrid search
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

    _logger.LogInformation("Retrieved {Count} passages for query: {Query}", passages.Count, query);
    return passages;
}
```

**Subtasks**:
- ☐ TODO-1.4.1: Add RetrievePassages method with hybrid search

**Test**: Index passages, verify retrieval returns relevant results

---

#### ⬜ Task 1.5: Implement MMR Diversity Filter (1 day)

**File to Modify**: `Services/FieldExtractor.cs`

**Methods to Add** (Per Proposal Lines 2047-2147):
```csharp
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

    var denominator = Math.Sqrt(magA) * Math.Sqrt(magB);
    return denominator > 0 ? dot / denominator : 0.0;
}

private List<Passage> ApplyMMR(
    List<(Passage passage, double score, float[]? vector)> ranked,
    float[] queryEmbedding,
    int maxPassages,
    double lambda)
{
    var selected = new List<(Passage passage, float[]? vector)>();
    var remaining = ranked.ToList();

    while (selected.Count < maxPassages && remaining.Count > 0)
    {
        double bestScore = double.MinValue;
        (Passage passage, double score, float[]? vector)? bestCandidate = null;
        int bestIndex = -1;

        for (int i = 0; i < remaining.Count; i++)
        {
            var candidate = remaining[i];
            var relevance = candidate.score;

            // Diversity penalty: max similarity to selected
            var maxSimilarity = 0.0;
            if (selected.Count > 0 && candidate.vector is { Length: > 0 })
            {
                foreach (var sel in selected)
                {
                    if (sel.vector is { Length: > 0 })
                    {
                        var sim = CosineSimilarity(candidate.vector!, sel.vector!);
                        maxSimilarity = Math.Max(maxSimilarity, sim);
                    }
                }
            }

            // MMR score: λ * relevance - (1-λ) * max_similarity
            var mmrScore = lambda * relevance - (1 - lambda) * maxSimilarity;

            if (mmrScore > bestScore)
            {
                bestScore = mmrScore;
                bestCandidate = candidate;
                bestIndex = i;
            }
        }

        if (bestCandidate.HasValue)
        {
            selected.Add((bestCandidate.Value.passage, bestCandidate.Value.vector));
            remaining.RemoveAt(bestIndex);
        }
        else break;
    }

    _logger.LogDebug("MMR selected {Count} diverse passages from {Total} candidates",
        selected.Count, ranked.Count);

    return selected.Select(pair => pair.passage).ToList();
}
```

**Subtasks**:
- ☐ TODO-1.5.1: Add CosineSimilarity helper
- ☐ TODO-1.5.2: Implement ApplyMMR method

**Test**: Mock passages, verify diversity selection

---

#### ⬜ Task 1.6: Implement Token Budget Management (0.5 day)

**File to Modify**: `Services/FieldExtractor.cs`

**Methods to Add**:
```csharp
private int EstimateTokenCount(string text)
{
    return text.Length / 4; // Rough approximation
}

private List<Passage> EnforceTokenBudget(List<Passage> passages, int maxTokens)
{
    var estimatedTokens = 0;
    var selected = new List<Passage>();

    foreach (var passage in passages)
    {
        var passageTokens = EstimateTokenCount(passage.Text);
        if (estimatedTokens + passageTokens <= maxTokens)
        {
            selected.Add(passage);
            estimatedTokens += passageTokens;
        }
        else break;
    }

    // Always include at least 1 passage
    if (selected.Count == 0 && passages.Count > 0)
    {
        selected.Add(passages[0]);
        estimatedTokens = EstimateTokenCount(passages[0].Text);
    }

    _logger.LogDebug("Token budget: {Actual} tokens (limit: {MaxTokens}), {Count} passages included",
        estimatedTokens, maxTokens, selected.Count);

    return selected;
}
```

**Subtasks**:
- ☐ TODO-1.6.1: Add EstimateTokenCount helper
- ☐ TODO-1.6.2: Implement EnforceTokenBudget method

**Test**: Verify budget enforced, minimum 1 passage

---

#### ⬜ Task 1.7: Implement LLM-Based Extraction (2 days) - CRITICAL

**File to Modify**: `Services/FieldExtractor.cs`

**Prompt Template** (Per Proposal Lines 3922-3964):
```csharp
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
```

**Robust JSON Parsing** (S6.SnapVault Pattern):
```csharp
private (string? Value, double Confidence, int? PassageIndex)? ParseExtractionResponse(string response)
{
    JObject? json = null;
    var jsonText = response.Trim();

    // Strategy 1: Direct parse
    json = TryParseJson(jsonText);

    if (json == null)
    {
        // Strategy 2: Strip markdown code blocks
        jsonText = Regex.Replace(response, @"```(?:json)?\s*|\s*```", "");
        json = TryParseJson(jsonText);
    }

    if (json == null)
    {
        // Strategy 3: Extract by balanced braces
        jsonText = ExtractJsonByBalancedBraces(response);
        json = TryParseJson(jsonText);
    }

    if (json == null)
    {
        _logger.LogWarning("All JSON parsing strategies failed for response: {Response}", response);
        return null;
    }

    var value = json["value"]?.ToString();
    var confidence = json["confidence"]?.Value<double>() ?? 0.0;
    var passageIndex = json["passageIndex"]?.Value<int>();

    return (value, confidence, passageIndex);
}

private JObject? TryParseJson(string text)
{
    try { return JObject.Parse(text); }
    catch { return null; }
}

private string ExtractJsonByBalancedBraces(string text)
{
    var depth = 0;
    var startIndex = -1;

    for (int i = 0; i < text.Length; i++)
    {
        if (text[i] == '{')
        {
            if (depth == 0) startIndex = i;
            depth++;
        }
        else if (text[i] == '}')
        {
            depth--;
            if (depth == 0 && startIndex >= 0)
            {
                return text.Substring(startIndex, i - startIndex + 1);
            }
        }
    }

    return text;
}
```

**AI Call with Ollama**:
```csharp
private async Task<ExtractedField?> ExtractFromPassages(
    DocumentPipeline pipeline,
    string fieldPath,
    JSchema fieldSchema,
    List<Passage> passages,
    MeridianOptions options,
    CancellationToken ct)
{
    if (passages.Count == 0) return null;

    // 1. Build prompt
    var prompt = BuildExtractionPrompt(passages, fieldPath, fieldSchema);

    // 2. Log prompt hash for reproducibility
    var promptHash = ComputePromptHash(prompt);
    _logger.LogDebug("Extraction prompt hash for {FieldPath}: {Hash}", fieldPath, promptHash);

    // 3. Call LLM
    var chatOptions = new AiChatOptions
    {
        Message = prompt,
        Temperature = options.Extraction.Temperature,
        MaxTokens = options.Extraction.MaxOutputTokens,
        Model = options.Extraction.Model ?? "granite3.3:8b"
    };

    _logger.LogDebug("Calling AI for field {FieldPath} with model {Model}", fieldPath, chatOptions.Model);
    var response = await Koan.AI.Ai.Chat(chatOptions, ct);

    // 4. Parse response
    var parsed = ParseExtractionResponse(response);
    if (parsed == null)
    {
        _logger.LogWarning("Failed to parse AI response for field {FieldPath}", fieldPath);
        return null;
    }

    // 5. Validate against schema
    var schemaValid = ValidateAgainstSchema(parsed.Value.Value, fieldSchema, out var validationError);

    // 6. Get best passage
    var passageIndex = parsed.Value.PassageIndex ?? 0;
    if (passageIndex < 0 || passageIndex >= passages.Count)
        passageIndex = 0;

    var bestPassage = passages[passageIndex];

    // 7. Locate span
    var span = LocateSpanInPassage(bestPassage.Text, parsed.Value.Value ?? "");

    // 8. Create ExtractedField
    var extraction = new ExtractedField
    {
        PipelineId = pipeline.Id,
        FieldPath = fieldPath,
        ValueJson = parsed.Value.Value,
        Confidence = parsed.Value.Confidence,
        SourceDocumentId = bestPassage.SourceDocumentId,
        PassageId = bestPassage.Id,
        Evidence = new TextSpanEvidence
        {
            PassageId = bestPassage.Id,
            SourceDocumentId = bestPassage.SourceDocumentId,
            OriginalText = bestPassage.Text,
            Page = bestPassage.PageNumber,
            Section = bestPassage.Section,
            Span = span
        },
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    _logger.LogInformation("Extracted field {FieldPath}: {Value} (confidence: {Confidence:P0})",
        fieldPath, parsed.Value.Value ?? "null", parsed.Value.Confidence);

    return extraction;
}

private string ComputePromptHash(string prompt)
{
    using var sha256 = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(prompt);
    var hash = sha256.ComputeHash(bytes);
    return Convert.ToHexString(hash).Substring(0, 12);
}
```

**Subtasks**:
- ☐ TODO-1.7.1: Add BuildExtractionPrompt method
- ☐ TODO-1.7.2: Add ComputePromptHash helper
- ☐ TODO-1.7.3: Add ParseExtractionResponse with 3-strategy parsing
- ☐ TODO-1.7.4: Add ValidateAgainstSchema with type repair
- ☐ TODO-1.7.5: Implement ExtractFromPassages main method

**Test**: Integration test with real Ollama granite3.3:8b

---

#### ⬜ Task 1.8: Implement Text Span Localization (0.5 day)

**File to Modify**: `Services/FieldExtractor.cs`

**Methods to Add** (Per Proposal Lines 2202-2299):
```csharp
private TextSpan? LocateSpanInPassage(string passageText, string extractedValue)
{
    if (string.IsNullOrWhiteSpace(passageText) || string.IsNullOrWhiteSpace(extractedValue))
        return null;

    // Strategy 1: Exact match
    var exactIndex = passageText.IndexOf(extractedValue, StringComparison.OrdinalIgnoreCase);
    if (exactIndex >= 0)
    {
        _logger.LogDebug("Span located via exact match");
        return new TextSpan { Start = exactIndex, End = exactIndex + extractedValue.Length };
    }

    // Strategy 2: Numeric normalization
    if (TryLocateNumeric(passageText, extractedValue, out var numericSpan))
    {
        _logger.LogDebug("Span located via numeric normalization");
        return numericSpan;
    }

    // Strategy 3: Regex patterns
    if (TryExtractWithRegex(passageText, extractedValue, out var regexSpan))
    {
        _logger.LogDebug("Span located via regex pattern");
        return regexSpan;
    }

    _logger.LogDebug("No span found for value: {Value}", extractedValue);
    return null;
}

private bool TryLocateNumeric(string passageText, string value, out TextSpan? span)
{
    span = null;
    var normalizedValue = NormalizeNumeric(value);
    if (normalizedValue == null) return false;

    var numberRegex = new Regex(@"[-+]?\$?[\d,]+\.?\d*[MKB]?");
    foreach (Match match in numberRegex.Matches(passageText))
    {
        if (NormalizeNumeric(match.Value) == normalizedValue)
        {
            span = new TextSpan { Start = match.Index, End = match.Index + match.Length };
            return true;
        }
    }
    return false;
}

private string? NormalizeNumeric(string value)
{
    var normalized = value.Replace("$", "").Replace(",", "").Trim();

    if (normalized.EndsWith("M", StringComparison.OrdinalIgnoreCase))
    {
        if (double.TryParse(normalized.TrimEnd('M', 'm'), out var num))
            return (num * 1_000_000).ToString();
    }
    else if (normalized.EndsWith("K", StringComparison.OrdinalIgnoreCase))
    {
        if (double.TryParse(normalized.TrimEnd('K', 'k'), out var num))
            return (num * 1_000).ToString();
    }

    return double.TryParse(normalized, out _) ? normalized : null;
}

private bool TryExtractWithRegex(string passageText, string value, out TextSpan? span)
{
    span = null;

    // Currency pattern
    if (value.Contains("$") || value.Contains("M") || value.Contains("K"))
    {
        var currencyRegex = new Regex(@"\$[\d,\.]+[MKB]?", RegexOptions.IgnoreCase);
        var match = currencyRegex.Match(passageText);
        if (match.Success)
        {
            span = new TextSpan { Start = match.Index, End = match.Index + match.Length };
            return true;
        }
    }

    // Date pattern
    if (DateTime.TryParse(value, out _))
    {
        var dateRegex = new Regex(@"\d{4}-\d{2}-\d{2}|\w{3}\s+\d{1,2},?\s+\d{4}|\d{1,2}/\d{1,2}/\d{4}");
        var match = dateRegex.Match(passageText);
        if (match.Success)
        {
            span = new TextSpan { Start = match.Index, End = match.Index + match.Length };
            return true;
        }
    }

    return false;
}
```

**Subtasks**:
- ☐ TODO-1.8.1: Implement LocateSpanInPassage with 3 strategies

**Test**: Verify spans found for currency, dates, exact matches

---

#### ⬜ Task 1.9: Wire into PipelineProcessor (0.5 day)

**Files to Modify**:
1. `Services/PipelineProcessor.cs`
2. `Services/FieldExtractor.cs`
3. `Initialization/KoanAutoRegistrar.cs`

**Changes in PipelineProcessor**:
```csharp
private readonly MeridianOptions _options;

public PipelineProcessor(
    // ... existing dependencies
    MeridianOptions options)
{
    // ... existing assignments
    _options = options;
}

// In ProcessAsync:
var extractions = await _fieldExtractor.ExtractAsync(pipeline, allPassages, _options, ct);
```

**Update FieldExtractor Signature**:
```csharp
public async Task<List<ExtractedField>> ExtractAsync(
    DocumentPipeline pipeline,
    IReadOnlyList<Passage> passages,
    MeridianOptions options,
    CancellationToken ct)
{
    var schema = pipeline.TryParseSchema();
    var results = new List<ExtractedField>();

    if (schema == null)
    {
        _logger.LogWarning("Pipeline {PipelineId} schema invalid; skipping extraction.", pipeline.Id);
        return results;
    }

    var fieldPaths = EnumerateLeafSchemas(schema).ToList();
    _logger.LogInformation("Extracting {Count} fields for pipeline {PipelineId}", fieldPaths.Count, pipeline.Id);

    foreach (var (fieldPath, fieldSchema) in fieldPaths)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Build RAG query
        var query = BuildRAGQuery(fieldPath, fieldSchema, pipeline);

        // 2. Retrieve relevant passages
        var retrieved = await RetrievePassages(pipeline.Id, query, options, ct);
        if (retrieved.Count == 0) continue;

        // 3. Apply MMR diversity (skip if vectors not accessible from VectorWorkflow)
        var diverse = retrieved; // TODO: Implement MMR when vector access available

        // 4. Enforce token budget
        var budgeted = EnforceTokenBudget(diverse, options.Retrieval.MaxTokensPerField);

        // 5. Extract from passages
        var extraction = await ExtractFromPassages(pipeline, fieldPath, fieldSchema, budgeted, options, ct);
        if (extraction != null)
        {
            results.Add(extraction);
        }
    }

    _logger.LogInformation("Extracted {Count} fields for pipeline {PipelineId}", results.Count, pipeline.Id);
    return results;
}
```

**Register Options in DI**:
```csharp
// In KoanAutoRegistrar.Initialize()
services.Configure<MeridianOptions>(
    services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection("Meridian"));
services.AddSingleton(sp => sp.GetRequiredService<IOptions<MeridianOptions>>().Value);
```

**Subtasks**:
- ☐ TODO-1.9.1: Inject MeridianOptions into PipelineProcessor
- ☐ TODO-1.9.2: Pass options to FieldExtractor
- ☐ TODO-1.9.3: Update FieldExtractor.ExtractAsync signature
- ☐ TODO-1.9.4: Bind MeridianOptions in KoanAutoRegistrar

**Test**: End-to-end pipeline compiles and runs

---

#### ⬜ Task 1.E2E: End-to-End Integration Test

**File to Create**: `tests/S7.Meridian.Tests/Integration/PipelineE2ETests.cs`

**Test Scenario**:
```csharp
[Fact]
public async Task EndToEnd_UploadExtractMergeRender_Success()
{
    // 1. Create pipeline
    var pipeline = new DocumentPipeline
    {
        Name = "Test Pipeline",
        SchemaJson = @"{
            ""type"": ""object"",
            ""properties"": {
                ""revenue"": {""type"": ""number""},
                ""employees"": {""type"": ""number""}
            }
        }",
        TemplateMarkdown = "# Test Report\n\nRevenue: {{revenue}}\nEmployees: {{employees}}"
    };
    await pipeline.Save(ct);

    // 2. Create test document
    var testContent = "Our company had annual revenue of $47.2M in FY2023. We have 150 employees.";
    // ... upload logic

    // 3. Create processing job
    var job = new ProcessingJob
    {
        PipelineId = pipeline.Id,
        DocumentIds = new List<string> { testDoc.Id },
        Status = JobStatus.Pending
    };
    await job.Save(ct);

    // 4. Process
    await processor.ProcessAsync(job, ct);

    // 5. Verify extractions
    var extractions = await ExtractedField.Query(e => e.PipelineId == pipeline.Id, ct);
    Assert.Equal(2, extractions.Count);

    var revenue = extractions.FirstOrDefault(e => e.FieldPath == "$.revenue");
    Assert.NotNull(revenue);
    Assert.True(revenue.Confidence > 0.5); // AI confidence
    Assert.NotNull(revenue.PassageId);

    // 6. Verify deliverable
    var deliverable = await Deliverable.Query(d => d.PipelineId == pipeline.Id, ct).FirstOrDefaultAsync();
    Assert.NotNull(deliverable);
    Assert.Contains("47", deliverable.Markdown); // Value present
    Assert.Contains("150", deliverable.Markdown);
}
```

**Subtasks**:
- ☐ TODO-1.E2E: Complete end-to-end integration test

**Acceptance**: Full pipeline works with real Ollama

---

## ✅ PHASE 1 COMPLETION CRITERIA

**Definition of Done**:
- [ ] All 53 checkboxes ticked ✅
- [ ] `dotnet build` succeeds with no warnings
- [ ] Integration test passes with real Ollama (granite3.3:8b)
- [ ] Upload test document → extracts fields with AI confidence scores
- [ ] Cache hit rate >80% on second identical document
- [ ] Logs show RAG pipeline stages (query, retrieve, extract)
- [ ] Code reviewed and approved

**Metrics to Verify**:
```bash
# Cache performance
Embedding cache: 0 hits, 10 misses (10 total)     # First run
Embedding cache: 10 hits, 0 misses (10 total)     # Second run

# Extraction quality
Extracted field $.revenue: "47.2" (confidence: 87%)
Extracted field $.employees: "150" (confidence: 92%)

# Pipeline performance
Retrieved 5 passages for query: "Find information about revenue"
MMR selected 3 diverse passages from 5 candidates
Token budget: 1847 tokens (limit: 2000), 3 passages included
```

---

## 🔍 KEY PROPOSAL REFERENCES

### RAG Extraction Specification (Lines 1865-2300)

**Core Flow**:
```
1. EnumerateLeafSchemas(schema) → List<(fieldPath, fieldSchema)>
2. For each field:
   a. BuildRAGQuery(fieldPath, schema, pipeline) → semantic query
   b. RetrievePassages(pipelineId, query, options) → hybrid search
   c. ApplyMMR(passages, queryEmbedding, maxPassages) → diversity
   d. EnforceTokenBudget(passages, maxTokens) → limit context
   e. ExtractFromPassages(passages, fieldPath, schema, options) → LLM call
   f. ParseExtractionResponse(aiResponse) → (value, confidence, passageIndex)
   g. ValidateAgainstSchema(value, schema) → schemaValid
   h. LocateSpanInPassage(passage.Text, value) → TextSpan
   i. Create ExtractedField with evidence
3. Return List<ExtractedField>
```

**Hybrid Search Parameters**:
- TopK: 12 (retrieve more for noisy PDFs)
- Alpha: 0.5 (50% semantic, 50% keyword)
- BM25 + Vector fusion

**MMR Algorithm**:
- Lambda: 0.7 (70% relevance, 30% diversity)
- Iterate: Select passage with max(λ * relevance - (1-λ) * max_similarity_to_selected)

**Token Budget**:
- Max: 2000 tokens per field (~8000 characters)
- Estimate: 1 token ≈ 4 characters
- Minimum: Always include at least 1 passage

**Prompt Engineering**:
- Deterministic prompts (no randomness)
- Clear instructions: "Extract EXACT value, do NOT infer"
- JSON response format enforced
- Prompt hash logged for reproducibility

---

## 📚 CODE PATTERNS FROM S5/S6

### S5.Recs: Embedding Cache Pattern
```csharp
// SHA-256 content-addressed cache
var contentHash = EmbeddingCache.ComputeContentHash(passage.Text);
var cached = await _cache.GetAsync(contentHash, "granite3.3:8b", "Passage", ct);

if (cached != null)
{
    _logger.LogDebug("Cache HIT");
    return cached.Embedding;
}

var embedding = await Koan.AI.Ai.Embed(passage.Text, ct);
await _cache.SetAsync(contentHash, "granite3.3:8b", embedding, "Passage", ct);
```

### S6.SnapVault: Robust JSON Parsing
```csharp
// 3-strategy parsing
var json = TryParseJson(response);                           // Direct
if (json == null) json = TryParseJson(StripMarkdown(response));  // Strip ```json```
if (json == null) json = TryParseJson(ExtractByBraces(response)); // Balanced braces
```

### S6.SnapVault: AI Chat with Options
```csharp
var chatOptions = new AiChatOptions
{
    Message = prompt,
    Temperature = 0.3,     // Low for determinism
    MaxTokens = 500,
    Model = "granite3.3:8b"
};

var response = await Koan.AI.Ai.Chat(chatOptions, ct);
```

---

## 🚨 COMMON PITFALLS TO AVOID

1. **Don't skip cache checks** - Every embedding should check cache first
2. **Don't hardcode confidence** - Must come from AI response
3. **Don't forget pipeline filtering** - Always filter passages by `PipelineId`
4. **Don't skip schema validation** - Validate all extracted values
5. **Don't ignore null responses** - LLM may return null for unfound fields
6. **Don't forget logging** - Log every stage (query, retrieve, extract)
7. **Don't skip error handling** - Wrap AI calls in try-catch
8. **Don't use synchronous I/O** - All DB/AI calls must be async

---

## 📋 DAILY RESUME CHECKLIST

When resuming work each day:

1. **Read this section**: Current Status Snapshot (top of document)
2. **Check last checkpoint**: See which task/subtask was last completed
3. **Review current task**: Read full task specification from relevant section
4. **Verify configuration**: Ensure appsettings.json values are correct
5. **Run Ollama check**: `ollama list | grep granite3.3`
6. **Check build status**: `dotnet build` should succeed
7. **Review logs**: Check for warnings from previous session
8. **Continue from checkpoint**: Pick up from first unchecked ☐ box

---

## 🎯 QUICK REFERENCE: Key Commands

### Build & Test
```bash
cd F:/Replica/NAS/Files/repo/github/koan-framework/samples/S7.Meridian
dotnet build
dotnet test
```

### Ollama Verification
```bash
ollama list | grep granite3.3
ollama run granite3.3:8b "What is 2+2?"
```

### Cache Stats
```bash
# Check cache directory
ls -la cache/embeddings/
# Count cached embeddings
find cache/embeddings -name "*.json" | wc -l
```

### Pipeline Execution (Manual Test)
```bash
# 1. Start app
dotnet run

# 2. Create pipeline (via Swagger or curl)
POST /api/pipelines

# 3. Upload document
POST /api/pipelines/{id}/documents

# 4. Check job status
GET /api/pipelines/{id}/jobs/{jobId}

# 5. Get deliverable
GET /api/pipelines/{id}/deliverables/latest
```

---

## 📝 PROGRESS TRACKING

Update this section after each task completion:

### Task Completion Log
```
[ ] SETUP-1: Verify Ollama Configuration
[x] Task 1.1: Port Embedding Cache (4 subtasks)
[x] Task 1.2: Enhance PassageIndexer (2 subtasks)
[x] Task 1.3: RAG Query Builder (1 subtask)
[x] Task 1.4: Hybrid Vector Search (1 subtask)
[x] Task 1.5: MMR Diversity Filter (2 subtasks)
[x] Task 1.6: Token Budget Management (2 subtasks)
[x] Task 1.7: LLM-Based Extraction (5 subtasks)
[x] Task 1.8: Text Span Localization (1 subtask)
[x] Task 1.9: Wire into Pipeline (4 subtasks)
[x] Task 1.E2E: End-to-End Test (1 subtask)
```

**Last Updated**: 2025-10-21
**Last Completed Task**: Task 1.E2E - End-to-End Test validated
**Next Task**: Phase 2 kickoff – Merge policy enhancements


---

## 🔄 CHECKPOINT RESTORE

If resuming after interruption, use this section to restore context:

**Current Implementation State**:
- FieldExtractor implements full RAG loop (hybrid retrieval, MMR, schema validation, span mapping)
- Embedding cache + PassageIndexer reuse vectors (SHA-256 hashed directories)
- PipelineProcessor injects MeridianOptions and defers persistence to processor stage
- Unit tests cover embedding cache + schema normalization helpers

**Next Immediate Actions**:
1. Build end-to-end harness to drive pipeline and assert deliverable output
2. Capture sample document fixtures for regression
3. Document manual verification steps in TESTING.md

**Known Blockers**: None (awaiting E2E harness)
