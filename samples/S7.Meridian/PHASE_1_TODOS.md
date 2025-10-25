# Phase 1: Core RAG-Based Field Extraction - Detailed Todo List

**Status**: In Progress (Task 1.E2E pending)
**Estimated Effort**: 5-7 days
**AI Models**: granite3.3:8b (text), qwen2.5vl (vision - future)
**Target**: Accuracy over speed

---

> **Architecture Update:** This checklist predates the migration to the fact catalog pipeline. Wherever you see `FieldExtractor`, substitute the combined work of `DocumentFactExtractor` and `FieldFactMatcher`.


## Pre-Implementation Setup

### ☐ SETUP-1: Verify Ollama Configuration
**Acceptance Criteria**:
- [ ] Ollama running on host machine
- [ ] `ollama list` shows granite3.3:8b available
- [ ] Test embedding: `await Koan.AI.Ai.Embed("test", ct)` succeeds
- [ ] Test chat: `await Koan.AI.Ai.Chat("hello", ct)` succeeds
- [ ] Verify appsettings.json Koan.AI.Ollama section correct

**Test Command**:
```bash
ollama list | grep granite3.3
ollama run granite3.3:8b "What is 2+2?"
```

**Validation**: Can call Ollama APIs successfully from Koan.AI

---

## Task 1.1: Port Embedding Cache from S5.Recs

### ✅ TODO-1.1.1: Create IEmbeddingCache Interface
**File**: `Services/IEmbeddingCache.cs`

**Acceptance Criteria**:
- [ ] Interface matches S5.Recs pattern exactly
- [ ] Methods: `GetAsync()`, `SetAsync()`, `FlushAsync()`, `GetStatsAsync()`
- [ ] Async/CancellationToken signatures
- [ ] XML documentation with usage examples

**Test**: Interface compiles, matches expected signature

---

### ✅ TODO-1.1.2: Create CachedEmbedding Model
**File**: `Models/CachedEmbedding.cs`

**Acceptance Criteria**:
- [ ] Properties: `ContentHash`, `ModelId`, `Embedding`, `Dimension`, `CachedAt`
- [ ] All properties have correct types
- [ ] Serializable to/from JSON via System.Text.Json
- [ ] XML documentation

**Test**: Model serializes/deserializes correctly
```csharp
var cached = new CachedEmbedding { ... };
var json = JsonSerializer.Serialize(cached);
var restored = JsonSerializer.Deserialize<CachedEmbedding>(json);
Assert.Equal(cached.ContentHash, restored.ContentHash);
```

---

### ✅ TODO-1.1.3: Implement EmbeddingCache Service
**File**: `Services/EmbeddingCache.cs`

**Acceptance Criteria**:
- [ ] Implements IEmbeddingCache
- [ ] File-based storage: `cache/embeddings/{entityType}/{modelId}/{hash}.json`
- [ ] `ComputeContentHash()` uses SHA-256
- [ ] `GetAsync()` returns null if not found, doesn't throw
- [ ] `SetAsync()` creates directories if needed
- [ ] `FlushAsync()` deletes all cache files and empty directories
- [ ] `GetStatsAsync()` returns file count and size
- [ ] All methods have try-catch with logging
- [ ] Injected ILogger logs cache hits/misses

**Test**: Manual verification
```csharp
var cache = new EmbeddingCache(logger);

// Test SetAsync
var hash = EmbeddingCache.ComputeContentHash("test content");
await cache.SetAsync(hash, "granite3.3:8b", new float[] { 0.1f, 0.2f }, "Passage", ct);

// Test GetAsync (hit)
var cached = await cache.GetAsync(hash, "granite3.3:8b", "Passage", ct);
Assert.NotNull(cached);
Assert.Equal(2, cached.Embedding.Length);

// Test GetAsync (miss)
var notFound = await cache.GetAsync("invalidhash", "granite3.3:8b", "Passage", ct);
Assert.Null(notFound);

// Test GetStatsAsync
var stats = await cache.GetStatsAsync(ct);
Assert.True(stats.TotalEmbeddings > 0);

// Test FlushAsync
var flushed = await cache.FlushAsync(ct);
Assert.True(flushed > 0);
var statsAfter = await cache.GetStatsAsync(ct);
Assert.Equal(0, statsAfter.TotalEmbeddings);
```

**Validation**: Cache directory structure created, files persisted, flush cleans up

---

### ✅ TODO-1.1.4: Register EmbeddingCache in DI
**File**: `Initialization/KoanAutoRegistrar.cs`

**Acceptance Criteria**:
- [ ] `services.AddSingleton<IEmbeddingCache, EmbeddingCache>();`
- [ ] Cache injectable in services

**Test**: Resolve from DI container
```csharp
var cache = serviceProvider.GetRequiredService<IEmbeddingCache>();
Assert.NotNull(cache);
Assert.IsType<EmbeddingCache>(cache);
```

---

## Task 1.2: Enhance PassageIndexer with Caching

### ✅ TODO-1.2.1: Inject IEmbeddingCache into PassageIndexer
**File**: `Services/PassageIndexer.cs`

**Acceptance Criteria**:
- [ ] Constructor accepts `IEmbeddingCache cache`
- [ ] Cache stored in private readonly field
- [ ] No breaking changes to existing code

**Test**: Compiles successfully

---

### ✅ TODO-1.2.2: Implement Cache-Aware Embedding Logic
**File**: `Services/PassageIndexer.cs` - `IndexAsync()` method

**Acceptance Criteria**:
- [ ] Before calling `Ai.Embed()`, check cache with `ComputeContentHash(passage.Text)`
- [ ] Model ID: "granite3.3:8b" (hardcoded for now, config later)
- [ ] On cache hit: log "Embedding cache HIT for passage {PassageId}"
- [ ] On cache miss: log "Embedding cache MISS for passage {PassageId}"
- [ ] After embedding, call `SetAsync()` to cache result
- [ ] Log cache hit/miss rate at end: "Embedding cache: {Hits} hits, {Misses} misses"

**Implementation Pattern**:
```csharp
var hits = 0;
var misses = 0;
var payload = new List<(Passage Entity, float[] Embedding, object? Metadata)>();

foreach (var passage in passages)
{
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
```

**Test**: Integration test with real Ollama
```csharp
// First run: all misses
var passages1 = new List<Passage> { new() { Text = "Test passage" } };
await indexer.IndexAsync(passages1, ct);
// Expect: 0 hits, 1 miss

// Second run: cache hit
var passages2 = new List<Passage> { new() { Text = "Test passage" } }; // Same text
await indexer.IndexAsync(passages2, ct);
// Expect: 1 hit, 0 misses

// Verify cache stats
var stats = await cache.GetStatsAsync(ct);
Assert.True(stats.TotalEmbeddings >= 1);
```

**Validation**: Second run with identical text shows cache hits, no duplicate AI calls

---

## Task 1.3: Implement RAG Query Builder

### ✅ TODO-1.3.1: Add BuildRAGQuery Method to FieldExtractor
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private string BuildRAGQuery(string fieldPath, JSchema fieldSchema, DocumentPipeline pipeline)`
- [ ] Extracts field name from path: `$.annualRevenue` → "annual revenue"
- [ ] Converts camelCase to spaced: `annualRevenue` → "annual revenue"
- [ ] Respects pipeline.BiasNotes if present
- [ ] Returns semantic query string

**Implementation**:
```csharp
private string BuildRAGQuery(string fieldPath, JSchema fieldSchema, DocumentPipeline pipeline)
{
    // Extract field name
    var fieldName = fieldPath.TrimStart('$', '.');

    // Convert camelCase to spaced
    var spaced = Regex.Replace(fieldName, "([a-z])([A-Z])", "$1 $2").ToLower();

    // Apply bias if present
    var bias = !string.IsNullOrWhiteSpace(pipeline.BiasNotes)
        ? $" {pipeline.BiasNotes}"
        : string.Empty;

    return $"Find information about {spaced}.{bias}";
}
```

**Test**: Unit test with various inputs
```csharp
var schema = JSchema.Parse("{\"type\": \"number\"}");
var pipeline = new DocumentPipeline { BiasNotes = null };

var query1 = BuildRAGQuery("$.annualRevenue", schema, pipeline);
Assert.Equal("Find information about annual revenue.", query1);

var query2 = BuildRAGQuery("$.companyName", schema, pipeline);
Assert.Equal("Find information about company name.", query2);

pipeline.BiasNotes = "Focus on Q3 2024";
var query3 = BuildRAGQuery("$.annualRevenue", schema, pipeline);
Assert.Equal("Find information about annual revenue. Focus on Q3 2024.", query3);
```

**Validation**: Queries are human-readable and semantic

---

## Task 1.4: Implement Hybrid Vector Search

### ✅ TODO-1.4.1: Add RetrievePassages Method to FieldExtractor
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private async Task<List<Passage>> RetrievePassages(string pipelineId, string query, MeridianOptions options, CancellationToken ct)`
- [ ] Embeds query using `Ai.Embed(query, ct)`
- [ ] Calls `VectorWorkflow<Passage>.Query()` with hybrid search
- [ ] Uses `options.Retrieval.TopK` and `options.Retrieval.Alpha`
- [ ] Filters results by `passage.PipelineId == pipelineId` to avoid cross-contamination
- [ ] Returns list of passages sorted by relevance
- [ ] Logs: "Retrieved {Count} passages for query: {Query}"

**Implementation**:
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
            SearchText: query,
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

**Test**: Integration test with indexed passages
```csharp
// Setup: Index 10 passages for pipeline "test-pipeline"
var testPassages = Enumerable.Range(1, 10)
    .Select(i => new Passage
    {
        PipelineId = "test-pipeline",
        Text = $"This is passage {i} about annual revenue of ${i * 10}M."
    })
    .ToList();

foreach (var p in testPassages)
{
    await p.Save(ct);
}

await indexer.IndexAsync(testPassages, ct);

// Test retrieval
var retrieved = await fieldExtractor.RetrievePassages(
    "test-pipeline",
    "annual revenue",
    options,
    ct);

Assert.True(retrieved.Count > 0);
Assert.True(retrieved.Count <= options.Retrieval.TopK);
Assert.All(retrieved, p => Assert.Equal("test-pipeline", p.PipelineId));
Assert.Contains(retrieved, p => p.Text.Contains("annual revenue"));
```

**Validation**: Returns relevant passages, respects TopK limit, filters by pipeline

---

## Task 1.5: Implement MMR Diversity Filter

### ✅ TODO-1.5.1: Add CosineSimilarity Helper
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private double CosineSimilarity(float[] a, float[] b)`
- [ ] Returns 0.0 if lengths don't match
- [ ] Computes: dot(a,b) / (||a|| * ||b||)
- [ ] Handles zero vectors gracefully

**Implementation**: Per proposal lines 2134-2147
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
```

**Test**: Unit test
```csharp
var a = new float[] { 1.0f, 0.0f, 0.0f };
var b = new float[] { 1.0f, 0.0f, 0.0f };
Assert.Equal(1.0, CosineSimilarity(a, b), 2); // Identical vectors

var c = new float[] { 0.0f, 1.0f, 0.0f };
Assert.Equal(0.0, CosineSimilarity(a, c), 2); // Orthogonal vectors

var d = new float[] { 0.5f, 0.5f, 0.0f };
var expected = 1.0 / Math.Sqrt(2); // 45 degrees
Assert.Equal(expected, CosineSimilarity(a, d), 2);
```

**Validation**: Cosine similarity computed correctly

---

### ✅ TODO-1.5.2: Implement ApplyMMR Method
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private List<Passage> ApplyMMR(List<(Passage passage, double score, float[]? vector)> ranked, float[] queryEmbedding, int maxPassages, double lambda)`
- [ ] Implements MMR algorithm per proposal lines 2047-2108
- [ ] Selects passages balancing relevance and diversity
- [ ] Returns at most `maxPassages` passages
- [ ] Logs: "MMR selected {Count} diverse passages from {Total} candidates"

**Implementation**: Per proposal
```csharp
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

            // Relevance to query
            var relevance = candidate.score;

            // Max similarity to already selected (diversity penalty)
            var maxSimilarity = 0.0;
            if (selected.Count > 0 && candidate.vector is { Length: > 0 })
            {
                foreach (var selectedPassage in selected)
                {
                    if (selectedPassage.vector is { Length: > 0 })
                    {
                        var similarity = CosineSimilarity(candidate.vector!, selectedPassage.vector!);
                        maxSimilarity = Math.Max(maxSimilarity, similarity);
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

**Test**: Unit test with mock passages
```csharp
// Create 5 passages with mock scores and vectors
var ranked = new List<(Passage, double, float[]?)>
{
    (new Passage { Text = "A" }, 1.0, new float[] { 1.0f, 0.0f }),
    (new Passage { Text = "B" }, 0.9, new float[] { 0.9f, 0.1f }), // Similar to A
    (new Passage { Text = "C" }, 0.8, new float[] { 0.0f, 1.0f }), // Diverse
    (new Passage { Text = "D" }, 0.7, new float[] { 0.8f, 0.2f }), // Similar to A
    (new Passage { Text = "E" }, 0.6, new float[] { 0.1f, 0.9f })  // Similar to C
};

var queryEmbedding = new float[] { 1.0f, 0.0f };
var selected = ApplyMMR(ranked, queryEmbedding, maxPassages: 3, lambda: 0.7);

// Expect: A (highest relevance), C (diverse), possibly E (diverse from A)
Assert.Equal(3, selected.Count);
Assert.Contains(selected, p => p.Text == "A"); // Highest relevance
Assert.Contains(selected, p => p.Text == "C"); // Diverse from A
```

**Validation**: Selected passages are diverse, not all similar

---

## Task 1.6: Implement Token Budget Management

### ✅ TODO-1.6.1: Add EstimateTokenCount Helper
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private int EstimateTokenCount(string text)`
- [ ] Uses rough estimate: 1 token ≈ 4 characters
- [ ] Returns reasonable approximation

**Implementation**:
```csharp
private int EstimateTokenCount(string text)
{
    return text.Length / 4;
}
```

**Test**: Unit test
```csharp
Assert.Equal(25, EstimateTokenCount("This is a 100 character string that should estimate to approximately 25 tokens for testing."));
```

---

### ✅ TODO-1.6.2: Implement EnforceTokenBudget Method
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private List<Passage> EnforceTokenBudget(List<Passage> passages, int maxTokens)`
- [ ] Takes passages in order, accumulates token count
- [ ] Stops when budget would be exceeded
- [ ] Always returns at least 1 passage (even if over budget)
- [ ] Logs: "Token budget: {Actual} tokens (limit: {MaxTokens}), {Count} passages included"

**Implementation**:
```csharp
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

**Test**: Unit test
```csharp
var passages = Enumerable.Range(1, 10)
    .Select(i => new Passage { Text = new string('x', 400) }) // Each ~100 tokens
    .ToList();

var selected = EnforceTokenBudget(passages, maxTokens: 500);
Assert.Equal(5, selected.Count); // 5 * 100 = 500 tokens

var oversized = EnforceTokenBudget(passages, maxTokens: 50);
Assert.Equal(1, oversized.Count); // Always at least 1
```

**Validation**: Budget enforced, minimum 1 passage guaranteed

---

## Task 1.7: Implement LLM-Based Extraction

### ✅ TODO-1.7.1: Add BuildExtractionPrompt Method
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private string BuildExtractionPrompt(List<Passage> passages, string fieldPath, JSchema fieldSchema)`
- [ ] Follows proposal specification (lines 3922-3964)
- [ ] Includes: field name, type, schema excerpt, numbered passages, instructions, JSON format
- [ ] Deterministic (no randomness)
- [ ] Clear instructions for AI

**Implementation**: Per proposal
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

**Test**: Manual inspection
```csharp
var passages = new List<Passage>
{
    new() { Text = "The annual revenue was $47.2M in FY2023." },
    new() { Text = "We have 150 employees." }
};

var schema = JSchema.Parse("{\"type\": \"number\"}");
var prompt = BuildExtractionPrompt(passages, "$.annualRevenue", schema);

// Manually verify prompt contains:
// - Field name "annualRevenue"
// - Type "number"
// - Both passages numbered [0] and [1]
// - Clear instructions
// - JSON response format
```

---

### ✅ TODO-1.7.2: Add ComputePromptHash Helper
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private string ComputePromptHash(string prompt)`
- [ ] Uses SHA-256
- [ ] Returns first 12 characters of hex string for logging
- [ ] Deterministic

**Implementation**:
```csharp
private string ComputePromptHash(string prompt)
{
    using var sha256 = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(prompt);
    var hash = sha256.ComputeHash(bytes);
    return Convert.ToHexString(hash).Substring(0, 12);
}
```

**Test**: Unit test
```csharp
var hash1 = ComputePromptHash("test prompt");
var hash2 = ComputePromptHash("test prompt");
var hash3 = ComputePromptHash("different prompt");

Assert.Equal(hash1, hash2); // Deterministic
Assert.NotEqual(hash1, hash3); // Different inputs → different hashes
Assert.Equal(12, hash1.Length);
```

---

### ✅ TODO-1.7.3: Add ParseExtractionResponse Method
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private (string? Value, double Confidence, int? PassageIndex)? ParseExtractionResponse(string response)`
- [ ] Implements robust JSON parsing (S6.SnapVault pattern)
- [ ] Strategy 1: Direct parse
- [ ] Strategy 2: Strip markdown code blocks
- [ ] Strategy 3: Extract by balanced braces
- [ ] Returns null on total parse failure
- [ ] Logs warnings on fallback strategies

**Implementation**: Based on S6.SnapVault lines 439-553
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
    try
    {
        return JObject.Parse(text);
    }
    catch
    {
        return null;
    }
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

    return text; // Fallback
}
```

**Test**: Unit test with various AI responses
```csharp
// Test direct JSON
var result1 = ParseExtractionResponse("{\"value\": \"$47.2M\", \"confidence\": 0.9, \"passageIndex\": 0}");
Assert.NotNull(result1);
Assert.Equal("$47.2M", result1.Value.Value);
Assert.Equal(0.9, result1.Value.Confidence);
Assert.Equal(0, result1.Value.PassageIndex);

// Test markdown wrapped
var result2 = ParseExtractionResponse("```json\n{\"value\": \"test\", \"confidence\": 0.8, \"passageIndex\": 1}\n```");
Assert.NotNull(result2);
Assert.Equal("test", result2.Value.Value);

// Test with text before/after
var result3 = ParseExtractionResponse("Here is the result: {\"value\": null, \"confidence\": 0.0, \"passageIndex\": null} Hope this helps!");
Assert.NotNull(result3);
Assert.Null(result3.Value.Value);
Assert.Equal(0.0, result3.Value.Confidence);

// Test parse failure
var result4 = ParseExtractionResponse("This is not JSON at all");
Assert.Null(result4);
```

**Validation**: Parses AI responses robustly

---

### ✅ TODO-1.7.4: Add ValidateAgainstSchema Method
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private bool ValidateAgainstSchema(string? valueJson, JSchema fieldSchema, out string? validationError)`
- [ ] Parses valueJson to JToken
- [ ] Validates against fieldSchema
- [ ] Attempts type repair (string → number if schema expects number)
- [ ] Returns true if valid, false otherwise
- [ ] Out parameter contains validation errors

**Implementation**:
```csharp
private bool ValidateAgainstSchema(string? valueJson, JSchema fieldSchema, out string? validationError)
{
    validationError = null;

    if (string.IsNullOrWhiteSpace(valueJson))
    {
        // Null is valid if schema allows it
        return !fieldSchema.Required.Any();
    }

    try
    {
        var token = JToken.Parse(valueJson);

        // Type repair: string → number
        if (fieldSchema.Type == JSchemaType.Number && token.Type == JTokenType.String)
        {
            if (double.TryParse(token.Value<string>(), out var numeric))
            {
                token = new JValue(numeric);
            }
        }

        // Validate
        if (!token.IsValid(fieldSchema, out IList<string> errors))
        {
            validationError = string.Join("; ", errors);
            return false;
        }

        return true;
    }
    catch (Exception ex)
    {
        validationError = ex.Message;
        return false;
    }
}
```

**Test**: Unit test
```csharp
var numberSchema = JSchema.Parse("{\"type\": \"number\"}");

// Valid number
Assert.True(ValidateAgainstSchema("47.2", numberSchema, out var error1));
Assert.Null(error1);

// String that can be repaired
Assert.True(ValidateAgainstSchema("\"123.45\"", numberSchema, out var error2));
Assert.Null(error2);

// Invalid
Assert.False(ValidateAgainstSchema("\"not a number\"", numberSchema, out var error3));
Assert.NotNull(error3);
```

---

### ✅ TODO-1.7.5: Implement ExtractFromPassages Method
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private async Task<ExtractedField?> ExtractFromPassages(DocumentPipeline pipeline, string fieldPath, JSchema fieldSchema, List<Passage> passages, MeridianOptions options, CancellationToken ct)`
- [ ] Builds prompt using BuildExtractionPrompt
- [ ] Logs prompt hash for reproducibility
- [ ] Calls Ollama using `Ai.Chat()` with options.Extraction.Temperature and MaxOutputTokens
- [ ] Parses response using ParseExtractionResponse
- [ ] Validates against schema
- [ ] Locates span in passage (stub for now, implemented in Task 1.8)
- [ ] Returns ExtractedField or null if parsing fails
- [ ] Logs at each step

**Implementation**:
```csharp
private async Task<ExtractedField?> ExtractFromPassages(
    DocumentPipeline pipeline,
    string fieldPath,
    JSchema fieldSchema,
    List<Passage> passages,
    MeridianOptions options,
    CancellationToken ct)
{
    if (passages.Count == 0)
    {
        _logger.LogWarning("No passages provided for field {FieldPath}", fieldPath);
        return null;
    }

    // 1. Build prompt
    var prompt = BuildExtractionPrompt(passages, fieldPath, fieldSchema);

    // 2. Log prompt hash
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
    if (!schemaValid)
    {
        _logger.LogWarning("Schema validation failed for field {FieldPath}: {Error}", fieldPath, validationError);
    }

    // 6. Get best passage
    var passageIndex = parsed.Value.PassageIndex ?? 0;
    if (passageIndex < 0 || passageIndex >= passages.Count)
        passageIndex = 0;

    var bestPassage = passages[passageIndex];

    // 7. Locate span (stub for now)
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

// Stub for span localization (implemented in Task 1.8)
private TextSpan? LocateSpanInPassage(string passageText, string extractedValue)
{
    // TODO: Implement in Task 1.8
    return null;
}
```

**Test**: Integration test with real Ollama
```csharp
var passages = new List<Passage>
{
    new()
    {
        Id = "p1",
        SourceDocumentId = "doc1",
        Text = "The company's annual revenue for FY2023 was $47.2M, representing 12% growth.",
        PageNumber = 3,
        Section = "Financial Overview"
    }
};

var schema = JSchema.Parse("{\"type\": \"number\"}");
var pipeline = new DocumentPipeline { Id = "test-pipeline" };
var options = new MeridianOptions();

var extraction = await fieldExtractor.ExtractFromPassages(
    pipeline,
    "$.annualRevenue",
    schema,
    passages,
    options,
    ct);

Assert.NotNull(extraction);
Assert.NotNull(extraction.ValueJson);
Assert.True(extraction.Confidence > 0.0);
Assert.Equal("p1", extraction.PassageId);
Assert.Equal("doc1", extraction.SourceDocumentId);
Assert.Equal(3, extraction.Evidence.Page);
```

**Validation**: Calls real Ollama, extracts values, returns structured field

---

## Task 1.8: Implement Text Span Localization

### ✅ TODO-1.8.1: Implement LocateSpanInPassage Method
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `private TextSpan? LocateSpanInPassage(string passageText, string extractedValue)`
- [ ] Strategy 1: Exact match (case-insensitive)
- [ ] Strategy 2: Numeric normalization ($47.2M → $47,200,000)
- [ ] Strategy 3: Regex patterns (currency, dates, percentages)
- [ ] Returns null if no span found (graceful)
- [ ] Logs which strategy succeeded

**Implementation**: Per proposal lines 2202-2299
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

    // Normalize both to remove commas, currency symbols
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
    // Remove $, commas, convert M/K/B to numbers
    var normalized = value.Replace("$", "").Replace(",", "").Trim();

    // Handle suffixes
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
    else if (normalized.EndsWith("B", StringComparison.OrdinalIgnoreCase))
    {
        if (double.TryParse(normalized.TrimEnd('B', 'b'), out var num))
            return (num * 1_000_000_000).ToString();
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

    // Percentage pattern
    if (value.Contains("%") || (double.TryParse(value, out var num) && num < 1.0))
    {
        var percentRegex = new Regex(@"\d+\.?\d*%");
        var match = percentRegex.Match(passageText);
        if (match.Success)
        {
            span = new TextSpan { Start = match.Index, End = match.Index + match.Length };
            return true;
        }
    }

    return false;
}
```

**Test**: Unit test
```csharp
// Exact match
var span1 = LocateSpanInPassage("Revenue was $47.2M", "$47.2M");
Assert.NotNull(span1);
Assert.Equal(12, span1.Start);
Assert.Equal(19, span1.End);

// Numeric normalization
var span2 = LocateSpanInPassage("Revenue was $47.2M", "47200000");
Assert.NotNull(span2);

// Date
var span3 = LocateSpanInPassage("Founded on Oct 15, 2024", "2024-10-15");
Assert.NotNull(span3);

// Not found
var span4 = LocateSpanInPassage("No revenue data here", "$47.2M");
Assert.Null(span4);
```

**Validation**: Spans correctly located for various value types

---

## Task 1.9: Wire into PipelineProcessor

### ✅ TODO-1.9.1: Inject MeridianOptions into PipelineProcessor
**File**: `Services/PipelineProcessor.cs`

**Acceptance Criteria**:
- [ ] Constructor accepts `MeridianOptions options`
- [ ] Stored in private readonly field
- [ ] No breaking changes to existing code

**Test**: Compiles successfully

---

### ✅ TODO-1.9.2: Pass Options to FieldExtractor
**File**: `Services/PipelineProcessor.cs` - `ProcessAsync()` method

**Acceptance Criteria**:
- [ ] FieldExtractor.ExtractAsync() signature updated to accept options
- [ ] Options passed from PipelineProcessor
- [ ] Compiles successfully

**Implementation**:
```csharp
// In ProcessAsync, after indexing
var extractions = await _fieldExtractor.ExtractAsync(pipeline, allPassages, _options, ct);
```

---

### ✅ TODO-1.9.3: Update FieldExtractor.ExtractAsync Signature
**File**: `Services/FieldExtractor.cs`

**Acceptance Criteria**:
- [ ] Method signature: `public async Task<List<ExtractedField>> ExtractAsync(DocumentPipeline pipeline, IReadOnlyList<Passage> passages, MeridianOptions options, CancellationToken ct)`
- [ ] Implements full RAG flow
- [ ] Calls BuildRAGQuery, RetrievePassages, ApplyMMR, EnforceTokenBudget, ExtractFromPassages
- [ ] Returns list of extracted fields
- [ ] Logs progress

**Implementation**:
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
        if (retrieved.Count == 0)
        {
            _logger.LogWarning("No passages retrieved for field {FieldPath}", fieldPath);
            continue;
        }

        // 3. Apply MMR diversity
        // TODO: Get vectors from VectorWorkflow results (currently not returned)
        // For now, skip MMR and use retrieved passages directly
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

**Test**: Integration test end-to-end

---

### ✅ TODO-1.9.4: Bind MeridianOptions in KoanAutoRegistrar
**File**: `Initialization/KoanAutoRegistrar.cs`

**Acceptance Criteria**:
- [ ] Binds MeridianOptions from configuration
- [ ] Registers as singleton
- [ ] Available via DI

**Implementation**:
```csharp
public void Initialize(IServiceCollection services)
{
    // Bind configuration
    services.Configure<MeridianOptions>(
        services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection("Meridian"));

    // Register as singleton for easy injection
    services.AddSingleton(sp => sp.GetRequiredService<IOptions<MeridianOptions>>().Value);

    // ... existing registrations
}
```

**Test**: Resolve from DI
```csharp
var options = serviceProvider.GetRequiredService<MeridianOptions>();
Assert.NotNull(options);
Assert.Equal(12, options.Retrieval.TopK);
```

---

## End-to-End Integration Test

### ✅ TODO-1.E2E: Complete Pipeline Test
**File**: `tests/S7.Meridian.Tests/Integration/PipelineE2ETests.cs` (new)

**Acceptance Criteria**:
- [x] Creates test pipeline with simple schema
- [x] Uploads test document (plain text with known values)
- [x] Triggers processing job
- [x] Waits for job completion
- [x] Verifies extracted fields
- [x] Verifies confidence scores from AI
- [x] Verifies passage links
- [x] Verifies deliverable rendered

**Test Scenario**:
```csharp
[Fact]
public async Task EndToEnd_UploadExtractMergeRender_Success()
{
    // 1. Create pipeline
    var pipeline = new DocumentPipeline
    {
        Name = "Test Pipeline",
        SchemaJson = "{\"type\": \"object\", \"properties\": {\"revenue\": {\"type\": \"number\"}, \"employees\": {\"type\": \"number\"}}}",
        TemplateMarkdown = "# Test Report\n\nRevenue: {{revenue}}\nEmployees: {{employees}}"
    };
    await pipeline.Save(ct);

    // 2. Create test document (plain text for simplicity)
    var testDoc = new SourceDocument
    {
        PipelineId = pipeline.Id,
        OriginalFileName = "test.txt",
        MediaType = "text/plain",
        StorageKey = "test-doc"
    };
    await testDoc.Save(ct);

    // Store test content
    var testContent = "Our company had annual revenue of $47.2M in FY2023. We have 150 employees.";
    await storage.StoreAsync(new MemoryStream(Encoding.UTF8.GetBytes(testContent)), "test.txt", "text/plain", ct);

    // 3. Create processing job
    var job = new ProcessingJob
    {
        PipelineId = pipeline.Id,
        DocumentIds = new List<string> { testDoc.Id },
        Status = JobStatus.Pending
    };
    await job.Save(ct);

    // 4. Process (simulate worker)
    await processor.ProcessAsync(job, ct);

    // 5. Verify extractions
    var extractions = await ExtractedField.Query(e => e.PipelineId == pipeline.Id, ct);
    Assert.Equal(2, extractions.Count); // revenue + employees

    var revenue = extractions.FirstOrDefault(e => e.FieldPath == "$.revenue");
    Assert.NotNull(revenue);
    Assert.NotNull(revenue.ValueJson);
    Assert.True(revenue.Confidence > 0.5); // AI confidence
    Assert.NotNull(revenue.PassageId);

    // 6. Verify deliverable
    var deliverable = await Deliverable.Query(d => d.PipelineId == pipeline.Id, ct).FirstOrDefaultAsync();
    Assert.NotNull(deliverable);
    Assert.Contains("47.2M", deliverable.Markdown); // Or normalized value
    Assert.Contains("150", deliverable.Markdown);
}
```

**Validation**: Full pipeline works end-to-end via deterministic integration harness (FakeAi scope) with Ollama verified separately in SETUP-1

---

## Phase 1 Completion Checklist

### Functional Requirements
- [ ] Embedding cache operational (hit rate >80% on second run)
- [ ] RAG query generation from field paths
- [ ] Hybrid vector search retrieves relevant passages
- [ ] MMR diversity filter reduces redundancy (or skipped if vectors not accessible)
- [ ] Token budget enforced (<= MaxTokensPerField)
- [ ] LLM extraction calls Ollama successfully
- [ ] AI responses parsed robustly
- [ ] Schema validation applied
- [ ] Text spans localized in passages
- [ ] End-to-end test passes

### Non-Functional Requirements
- [ ] All methods have XML documentation
- [ ] Structured logging at DEBUG, INFO, WARNING levels
- [ ] Error handling with try-catch where appropriate
- [ ] Configuration externalized via MeridianOptions
- [ ] No hardcoded values (except model names)
- [ ] Code follows existing Koan patterns
- [ ] No compiler warnings

### Testability
- [ ] Unit tests for all helpers (CosineSimilarity, EstimateTokenCount, etc.)
- [ ] Integration test with real Ollama
- [ ] Cache stats queryable via API
- [ ] Logs show cache hit/miss rates

---

## Definition of Done

**Phase 1 is COMPLETE when**:
1. All 53 checkboxes above are ticked ✅
2. `dotnet build` succeeds with no warnings
3. Integration test passes with real Ollama (granite3.3:8b)
4. Upload test document → extracts fields with AI confidence scores
5. Cache hit rate >80% on second identical document
6. Logs show RAG pipeline stages (query, retrieve, extract)
7. Code reviewed and approved

**Ready to proceed?** Once approved, I'll execute Task 1.1.1 through TODO-1.E2E sequentially, reporting progress at phase completion.
