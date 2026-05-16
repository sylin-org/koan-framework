# Koan.Context QA Evaluation Report

**Date**: 2025-11-05
**Scope**: Milestone 1-3 Implementation (Console App + Ingest Pipeline)
**Status**: ⚠️ **CRITICAL ISSUES FOUND** - Not production-ready
**Test Coverage**: ❌ **0% - NO TESTS EXIST**

---

## Executive Summary

The Koan.Context implementation successfully compiles and demonstrates the core architecture patterns. However, **critical security vulnerabilities, data integrity issues, and complete absence of tests make this unsuitable for production use**.

### Severity Distribution
- 🔴 **Critical**: 8 issues (Security, Data Loss, Crashes)
- 🟠 **High**: 12 issues (Bugs, Logic Errors, Resource Leaks)
- 🟡 **Medium**: 15 issues (Performance, Edge Cases)
- 🟢 **Low**: 10 issues (Code Quality, Maintainability)

**Total Issues**: 45

---

## Critical Issues (🔴 Must Fix Before Any Use)

### 1. **Path Traversal Vulnerability** (DocumentDiscoveryService.cs:40)
**Severity**: 🔴 Critical - Security
**Impact**: Arbitrary file system access

```csharp
var searchPath = string.IsNullOrWhiteSpace(docsPath)
    ? projectPath
    : Path.Combine(projectPath, docsPath);
```

**Problem**: If `docsPath` contains `".."` or absolute paths, it can escape `projectPath`.

**Attack Vector**:
```csharp
// Attacker provides:
docsPath = "../../../../etc"
// Results in: /path/to/project/../../../../etc → /etc
```

**Fix Required**:
```csharp
if (!string.IsNullOrWhiteSpace(docsPath))
{
    var normalizedDocs = Path.GetFullPath(Path.Combine(projectPath, docsPath));
    var normalizedProject = Path.GetFullPath(projectPath);

    if (!normalizedDocs.StartsWith(normalizedProject, StringComparison.OrdinalIgnoreCase))
    {
        throw new SecurityException($"docsPath escapes project boundary: {docsPath}");
    }

    searchPath = normalizedDocs;
}
```

---

### 2. **Data Loss: Batch Failure Discards All Chunks** (IndexingService.cs:147)
**Severity**: 🔴 Critical - Data Integrity
**Impact**: Up to 100 chunks lost on any vector save error

```csharp
await SaveVectorBatchAsync(batch, cancellationToken);
vectorsSaved += batch.Count;
batch.Clear();
```

**Problem**: If `SaveVectorBatchAsync` throws, the entire batch is lost. No retry, no partial save, no recovery.

**Scenario**:
1. Index 500 chunks successfully
2. Chunk 501-600: Weaviate connection fails
3. **Result**: 100 chunks lost, DocumentChunk records orphaned

**Fix Required**:
- Implement batch retry with exponential backoff
- Log failed chunks for manual recovery
- Consider transactional outbox pattern

---

### 3. **Unclosed Code Blocks Lost** (ContentExtractionService.cs:47-84)
**Severity**: 🔴 High - Data Loss
**Impact**: Markdown with unclosed ``` fence loses all code content

```csharp
if (codeFenceMatch.Success)
{
    if (!inCodeBlock)
    {
        inCodeBlock = true;
        codeBlockLines.Clear();
    }
    else
    {
        // Only saves if closing fence found
        sections.Add(...);
    }
}
```

**Problem**: If a file has:
```markdown
# Example
```python
def foo():
    return 42
```
(missing closing fence)

The Python code is **silently dropped**.

**Fix Required**:
- After loop, check `if (inCodeBlock)` and emit accumulated content
- Log warning about malformed markdown

---

### 4. **Offset Calculation Corruption** (ContentExtractionService.cs:135, 149)
**Severity**: 🔴 High - Incorrect Provenance
**Impact**: Chunk byte ranges point to wrong file locations

```csharp
while (i + 1 < lines.Length && ...)
{
    i++;
    currentOffset += lineLength;  // ← Incremented here
    line = lines[i];
    lineLength = line.Length + 1;
    paragraphLines.Add(line);
}
// ...
currentOffset += lineLength;  // ← Incremented AGAIN
```

**Problem**: Double-counting offsets causes all subsequent sections to have wrong start/end positions.

**Impact**:
- "Open in editor" links jump to wrong lines
- Chunk provenance corrupted
- Diff-based incremental indexing breaks

**Fix Required**:
- Remove duplicate increment at line 149
- Add unit test comparing offsets to actual file positions

---

### 5. **Substring Out of Bounds** (DocumentDiscoveryService.cs:126)
**Severity**: 🔴 High - Crash
**Impact**: Application crash if .git/HEAD is malformed

```csharp
if (headContent.StartsWith("ref:"))
{
    var refPath = headContent.Substring(5).Trim();  // ← Crash if headContent = "ref:"
}
```

**Problem**: If `.git/HEAD` contains only `"ref:"` (4 chars), `Substring(5)` throws `ArgumentOutOfRangeException`.

**Fix Required**:
```csharp
if (headContent.StartsWith("ref:") && headContent.Length > 5)
{
    var refPath = headContent.Substring(5).Trim();
    // ...
}
```

---

### 6. **Silent Exception Swallowing** (DocumentDiscoveryService.cs:141-144)
**Severity**: 🔴 High - Observability
**Impact**: Git errors invisible, debugging impossible

```csharp
catch
{
    // Ignore git read errors  ← NO LOGGING
}
```

**Problem**: If git read fails (permissions, corruption, etc.), operator has no visibility.

**Fix Required**:
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to read git commit SHA from {GitPath}", gitHeadPath);
}
```

---

### 7. **No Validation: Null Projectcontext Check** (IndexingService.cs:81)
**Severity**: 🔴 Medium - Unexpected Behavior
**Impact**: Partition context could be null, causing global indexing

```csharp
using (EntityContext.Partition(projectId.ToString()))
{
    // All vector operations
}
```

**Problem**: If `EntityContext.Partition()` returns null (implementation-dependent), vectors save globally instead of partitioned.

**Fix Required**:
- Add assertion: `Debug.Assert(EntityContext.Current?.Partition == projectId.ToString())`
- Integration test verifying partition isolation

---

### 8. **Cache Key Collision Possible** (EmbeddingService.cs:155-159)
**Severity**: 🔴 Low - Correctness
**Impact**: Different models could share cached embeddings

```csharp
var input = $"{text}|{model ?? "default"}";
var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
return $"embedding:{Convert.ToHexString(hashBytes)}";
```

**Problem**: If `model = null` for two different actual models, both use `"default"` in key.

**Fix Required**:
- Use `_defaultModel` instead of `model ?? "default"`
- Or include model version/hash in key

---

## High Priority Issues (🟠 Fix Before Production)

### 9. **No Transaction Rollback** (IndexingService.cs:125, 212)
**Problem**: DocumentChunk saved to relational DB, then vector save fails → orphaned metadata.

**Fix**: Use outbox pattern or two-phase commit.

---

### 10. **Duplicate Chunking Logic** (ChunkingService.cs:52-75, 93-120)
**Problem**: Same logic copied twice, risks inconsistency. 74 lines of duplication.

**Fix**: Extract to `YieldChunk(...)` method.

---

### 11. **Offset Calculation Wrong** (ChunkingService.cs:73, 117)
**Problem**: `section.EndOffset - overlapText.Length` assumes overlap from section, but overlap is from chunk text.

**Fix**: Track actual overlap source positions.

---

### 12. **Synchronous Directory Scan** (DocumentDiscoveryService.cs:61)
**Problem**: `Directory.EnumerateFiles` blocks async enumeration.

**Fix**: Use `Directory.EnumerateFiles(...).AsEnumerable()` with `Task.Yield()` or implement true async scan.

---

### 13. **File Access Not Protected** (DocumentDiscoveryService.cs:71)
**Problem**: `new FileInfo(file)` can throw `UnauthorizedAccessException`.

**Fix**: Wrap in try-catch, log warning, continue to next file.

---

### 14. **No Symlink Protection** (DocumentDiscoveryService.cs:61)
**Problem**: Symlink cycles cause infinite loops or stack overflow.

**Fix**: Check `fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)` and skip.

---

### 15. **Large File Memory Exhaustion** (ContentExtractionService.cs:25)
**Problem**: `ReadAllTextAsync` loads entire file into memory. A 2GB markdown file crashes the process.

**Fix**: Stream-based parsing or file size limit (e.g., max 50MB).

---

### 16. **Line Ending Mishandling** (ContentExtractionService.cs:42)
**Problem**: `Split('\n')` leaves `\r` at end of lines on Windows.

**Fix**: `Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)`

---

### 17. **Paragraph Regex Not Checked** (ContentExtractionService.cs:132)
**Problem**: Paragraph accumulation doesn't check for code fences, can merge fence into paragraph.

**Fix**: Add `&& !CodeFenceRegex.IsMatch(lines[i + 1])`

---

### 18. **Title Hierarchy Incomplete** (ContentExtractionService.cs:109-119)
**Problem**: Only handles H1 and H2, ignores H3-H6.

**Fix**: Implement full hierarchy stack with proper nesting.

---

### 19. **Massive Chunk If Section > Max** (ChunkingService.cs:50-75)
**Problem**: If single section is 5000 tokens, it's yielded as one chunk, breaking 800-1000 token target.

**Fix**: Split large sections at sentence boundaries.

---

### 20. **Token Count Ignores Newlines** (ChunkingService.cs:78-85)
**Problem**: `AppendLine()` twice adds ~2 chars, but token count doesn't include them.

**Fix**: `currentTokens += EstimateTokens("\n\n")`

---

## Medium Priority Issues (🟡 Fix Before Scale)

### 21. **No Rate Limit Handling** (EmbeddingService.cs:59)
**Problem**: If Ollama/OpenAI rate limits, request fails with no retry.

**Fix**: Exponential backoff with Polly library.

---

### 22. **Batch Order Not Preserved** (EmbeddingService.cs:94-150)
**Problem**: Cached results added first, then uncached, changing order.

**Fix**: Use `Dictionary<string, int>` to track original indices, restore order.

---

### 23. **No Empty File Validation** (ContentExtractionService.cs:25)
**Problem**: Empty markdown files create ExtractedDocument with 0 sections, wasting processing.

**Fix**: Check `fullText.Length > 0` before extraction.

---

### 24. **Dimension Hardcoded Wrong** (IndexingService.cs:173)
**Problem**: Assumes 1536 dimensions (OpenAI), but config uses all-minilm (384).

**Fix**: Read from configuration or IAi.GetEmbeddingDimension().

---

### 25. **No Cancellation Between Batches** (IndexingService.cs:147)
**Problem**: `cancellationToken` checked at file level (line 87), but not between batch saves.

**Fix**: Pass `cancellationToken` to `SaveVectorBatchAsync`.

---

### 26. **Progress Before Completion** (IndexingService.cs:92)
**Problem**: Progress shows "FilesProcessed: 5" before file 5 is done.

**Fix**: Report after successful processing (line 155).

---

### 27. **Batch List Not Bounded** (IndexingService.cs:83)
**Problem**: If exceptions prevent clearing, `batch` list grows unbounded.

**Fix**: Move `batch.Clear()` to finally block.

---

### 28. **No Logging for Empty Results** (IndexingService.cs:78)
**Problem**: If 0 files discovered, silent success.

**Fix**: `_logger.LogWarning("No markdown files discovered in {Path}", project.RootPath)`

---

### 29. **Static Method No Logger Access** (IndexingService.cs:206)
**Problem**: `SaveVectorBatchAsync` is static, can't log vector save details.

**Fix**: Make instance method, log batch size and partition.

---

### 30. **Cache Unbounded Growth** (EmbeddingService.cs:68-75)
**Problem**: IMemoryCache can grow infinitely if no size limits set globally.

**Fix**: Configure cache size limit in DI registration.

---

### 31. **No Retry on Embedding Failure** (EmbeddingService.cs:59)
**Problem**: Transient network failures abort entire indexing.

**Fix**: Retry with Polly (3 retries, exponential backoff).

---

### 32. **Overlap Logic Convoluted** (ChunkingService.cs:153-179)
**Problem**: Extra 100-char buffer, then searches for break points - unclear intent.

**Fix**: Simplify to "get last N tokens, break at sentence."

---

### 33. **ToString() Called Multiple Times** (ChunkingService.cs:55, 99, 128)
**Problem**: Same chunk converted to string 3x in different code paths.

**Fix**: Convert once, store in variable.

---

### 34. **No Empty Text Check** (EmbeddingService.cs:38-41)
**Problem**: Throws on empty text. Should it? Might want to skip instead.

**Decision Needed**: Define behavior for empty chunks.

---

### 35. **No Partial Failure Visibility** (IndexingService.cs:185-190)
**Problem**: IndexingResult.Errors is list of strings, doesn't show which files failed.

**Fix**: Use structured error: `record IndexError(string FilePath, string Message)`

---

## Low Priority Issues (🟢 Technical Debt)

### 36. **Regex Not Compiled** (ContentExtractionService.cs:15-16)
**Problem**: Regexes used in hot loop without `RegexOptions.Compiled`.

**Fix**: Add `RegexOptions.Compiled | RegexOptions.Multiline`.

---

### 37. **DI Registration Hardcoded Model** (KoanAutoRegistrar.cs:34)
**Problem**: `_defaultModel = "all-minilm"` hardcoded, should read from config.

**Fix**: `cfg.GetValue<string>("Koan:AI:Embedding:Model") ?? "all-minilm"`

---

### 38. **No Health Check** (Program.cs)
**Problem**: Comment mentions `/health` endpoint but it's not implemented.

**Fix**: Add `builder.Services.AddHealthChecks().AddCheck<WeaviateHealthCheck>()`

---

### 39. **No Null Check in Controller** (ProjectsController.cs:110)
**Problem**: `_indexingService.IndexProjectAsync(id)` doesn't validate ID format.

**Fix**: Add `[Required, Guid]` attribute or manual validation.

---

### 40. **No Request Validation** (ProjectsController.cs:42)
**Problem**: CreateProjectRequest fields not validated (max length, path format, etc.).

**Fix**: Use `[Required, StringLength(500)]`, `[ValidPath]` attributes.

---

### 41. **Error Messages Not Localized** (All services)
**Problem**: All error messages in English, no localization support.

**Fix**: Use resource files if i18n is a requirement.

---

### 42. **No Telemetry** (All services)
**Problem**: No metrics (chunks/sec, embedding latency, cache hit rate).

**Fix**: Add `ILogger` metrics and OpenTelemetry spans.

---

### 43. **No Rate Limit on API** (ProjectsController.cs)
**Problem**: `/api/projects/{id}/index` can be spammed, causing resource exhaustion.

**Fix**: Add rate limiting middleware (ASP.NET Core rate limiter).

---

### 44. **No Concurrency Control** (IndexingService.cs)
**Problem**: Two simultaneous index requests for same project can conflict.

**Fix**: Use distributed lock or queue-based processing.

---

### 45. **Discovery Pattern Not Used** (KoanAutoRegistrar.cs)
**Problem**: IncludedPatterns field defined but never used (line 20-28).

**Fix**: Remove dead code or implement pattern-based filtering.

---

## Test Coverage Analysis

### Current State
**Test Files**: 0
**Test Cases**: 0
**Code Coverage**: 0%

### Required Test Suites

#### Unit Tests (Missing)

**DocumentDiscoveryService**:
- ✗ Discovers .md files in docs/
- ✗ Excludes node_modules, bin, obj
- ✗ Handles symbolic links safely
- ✗ Throws on non-existent path
- ✗ Reads git commit SHA
- ✗ Returns null if .git missing
- ✗ Handles detached HEAD
- ✗ Handles ref: format
- ✗ Path traversal attack blocked

**ContentExtractionService**:
- ✗ Extracts headings (H1-H6)
- ✗ Extracts code blocks with language
- ✗ Handles unclosed code blocks
- ✗ Extracts paragraphs
- ✗ Builds title hierarchy correctly
- ✗ Handles Windows line endings (\r\n)
- ✗ Handles empty files
- ✗ Offset calculation accuracy
- ✗ Handles malformed markdown

**ChunkingService**:
- ✗ Creates chunks of 800-1000 tokens
- ✗ Adds 50-token overlap
- ✗ Respects heading boundaries
- ✗ Handles documents < 800 tokens
- ✗ Handles single section > 1000 tokens
- ✗ Overlap text at word boundaries
- ✗ Token estimation accuracy (±10%)

**EmbeddingService**:
- ✗ Caches embeddings by SHA256
- ✗ Cache hit returns same vector
- ✗ Cache miss calls IAi
- ✗ Batch processing optimizes cache lookups
- ✗ Throws on empty text
- ✗ Handles provider errors gracefully

**IndexingService**:
- ✗ Orchestrates full pipeline
- ✗ Sets partition context
- ✗ Saves batches of 100 chunks
- ✗ Reports progress accurately
- ✗ Collects errors per file
- ✗ Updates project metadata
- ✗ Handles cancellation

**ProjectsController**:
- ✗ GET /api/projects returns all
- ✗ POST /api/projects/create validates input
- ✗ POST /{id}/index triggers indexing
- ✗ Returns 404 for missing project
- ✗ Returns 400 for invalid input

#### Integration Tests (Missing)

**Koan.Tests.Context.Integration**:
- ✗ End-to-end indexing of sample repo
- ✗ Search returns correct chunks
- ✗ Partition isolation verified
- ✗ Weaviate class created correctly
- ✗ Incremental reindex (git diff-based)
- ✗ Error recovery after Weaviate restart
- ✗ Concurrent indexing of 2 projects

#### Performance Tests (Missing)

**Benchmarks**:
- ✗ Index 1000 .md files (target: <5 min)
- ✗ Search 100 queries (p95 < 100ms)
- ✗ Memory usage < 500MB for 10k chunks
- ✗ Cache hit rate > 80% on reindex

---

## Architectural Concerns

### 1. **No Idempotency**
**Issue**: Reindexing same files creates duplicate DocumentChunk records and vectors.

**Fix**: Use `(projectId, filePath, commitSha, chunkRange)` as unique constraint.

---

### 2. **No Incremental Indexing**
**Issue**: Reindex processes all files, even unchanged ones.

**Fix**: Store file hashes, compare before processing.

---

### 3. **No Schema Versioning**
**Issue**: If DocumentChunk model changes, existing data incompatible.

**Fix**: Add SchemaVersion field, implement migrations.

---

### 4. **No Observability**
**Issue**: No metrics, traces, or structured logging.

**Fix**: Add OpenTelemetry with custom metrics:
- `koan.context.files_indexed` (counter)
- `koan.context.embedding_latency` (histogram)
- `koan.context.cache_hit_rate` (gauge)

---

### 5. **No Configuration Validation**
**Issue**: If appsettings.json has invalid Weaviate endpoint, fails at runtime.

**Fix**: Validate config at startup, fail fast.

---

## Recommended Action Plan

### Phase 1: Critical Fixes (Week 1)
1. ✅ Fix path traversal vulnerability (Issue #1)
2. ✅ Add batch save retry logic (Issue #2)
3. ✅ Fix unclosed code block handling (Issue #3)
4. ✅ Fix offset calculation (Issue #4)
5. ✅ Fix substring bounds check (Issue #5)
6. ✅ Add logging to catch blocks (Issue #6)

### Phase 2: High Priority (Week 2)
7. ✅ Implement transaction safety (Issue #9)
8. ✅ Refactor duplicate chunking logic (Issue #10)
9. ✅ Fix async directory scan (Issue #12)
10. ✅ Add file access error handling (Issue #13)
11. ✅ Add symlink protection (Issue #14)
12. ✅ Add file size limit (Issue #15)

### Phase 3: Test Suite (Week 3-4)
13. ✅ Create unit test project
14. ✅ Write 80+ unit tests
15. ✅ Write 10+ integration tests
16. ✅ Achieve 80% code coverage
17. ✅ Add CI/CD with test enforcement

### Phase 4: Polish (Week 5)
18. ✅ Fix medium priority issues
19. ✅ Add telemetry
20. ✅ Performance benchmarks
21. ✅ Documentation

---

## Test Suite Specification

### File: `tests/Suites/Context/Unit/Koan.Tests.Context.Unit/Specs/Discovery/DocumentDiscovery.Spec.cs`

```csharp
public class DocumentDiscovery_Spec
{
    [Fact]
    public async Task DiscoverAsync_FindsMarkdownFiles()
    {
        // Arrange
        var tempDir = CreateTestDirectory(
            "README.md",
            "docs/guide.md",
            "src/code.cs"
        );
        var service = new DocumentDiscoveryService();

        // Act
        var files = await service.DiscoverAsync(tempDir).ToListAsync();

        // Assert
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Type == FileType.Readme);
        Assert.Contains(files, f => f.RelativePath == "docs/guide.md");
    }

    [Fact]
    public async Task DiscoverAsync_ExcludesNodeModules()
    {
        // Arrange
        var tempDir = CreateTestDirectory(
            "README.md",
            "node_modules/package/README.md"
        );
        var service = new DocumentDiscoveryService();

        // Act
        var files = await service.DiscoverAsync(tempDir).ToListAsync();

        // Assert
        Assert.Single(files);
        Assert.DoesNotContain(files, f => f.RelativePath.Contains("node_modules"));
    }

    [Fact]
    public async Task DiscoverAsync_ThrowsOnPathTraversal()
    {
        // Arrange
        var service = new DocumentDiscoveryService();

        // Act & Assert
        await Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await service.DiscoverAsync("/safe/path", "../../etc").ToListAsync();
        });
    }

    // 20+ more tests...
}
```

---

## Conclusion

The Koan.Context implementation demonstrates solid architectural thinking and successfully implements the "Reference = Intent" pattern. However, **it is not production-ready** due to:

1. **8 critical vulnerabilities** (security, data loss, crashes)
2. **27 high/medium bugs** (correctness, performance, edge cases)
3. **0% test coverage** (no unit, integration, or performance tests)
4. **No operational readiness** (missing health checks, metrics, logging)

**Estimated Work to Production**:
- **5 weeks** of development
- **80+ unit tests**
- **10+ integration tests**
- **Performance benchmarks**
- **Security audit**

**Current Grade**: D (Compiles, demonstrates concepts, but unsafe for use)
**Target Grade**: A (Production-ready with 80%+ coverage, security-hardened, performant)

---

**Next Steps**: Prioritize **Phase 1 critical fixes** before any user-facing deployment.
