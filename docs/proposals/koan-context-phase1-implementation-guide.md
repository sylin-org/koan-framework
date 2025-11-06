# Koan.Context Phase 1 Implementation Guide

**Status**: IN PROGRESS
**Phase**: 1 of 2
**Completed**: 2/12 tasks

## Progress Summary

### ✅ Completed
1. **Architecture Documentation** (`koan-context-ai-optimization-phase1.md`)
2. **Enhanced Models** (`IRetrievalService.cs`)
   - SearchResult with metadata, sources, insights, warnings
   - SearchResultChunk with IDs, provenance, reasoning
   - Token budget support (MaxTokens)
   - Continuation token structure

### 🔄 Remaining Tasks

## Task 3: TokenCountingService

**File**: `src/Koan.Context/Services/TokenCountingService.cs`

```csharp
namespace Koan.Context.Services;

public interface ITokenCountingService
{
    int EstimateTokens(string text);
    int EstimateTokens(IEnumerable<string> texts);
}

public class TokenCountingService : ITokenCountingService
{
    // Heuristic: 1 token ≈ 4 characters
    // Future: Integrate SharpToken/Tiktoken for GPT-accurate counts

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    public int EstimateTokens(IEnumerable<string> texts)
    {
        return texts.Sum(EstimateTokens);
    }
}
```

**Registration** in `Program.cs` or auto-registrar:
```csharp
builder.Services.AddSingleton<ITokenCountingService, TokenCountingService>();
```

## Task 4: Update ChunkingService for Byte Offsets

**File**: `src/Koan.Context/Services/ChunkingService.cs`

Update `ChunkFileAsync` return type:

```csharp
public record ChunkResult(
    string Text,
    int StartLine,
    int EndLine,
    long StartByteOffset,  // NEW
    long EndByteOffset     // NEW
);

public async IAsyncEnumerable<ChunkResult> ChunkFileAsync(
    string content,
    string language,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    long byteOffset = 0;
    int lineNumber = 1;
    var encoding = Encoding.UTF8;

    // ... existing chunking logic ...

    foreach (var chunkLines in ChunkByLogic(lines))
    {
        long startByte = byteOffset;
        string chunkText = string.Join("\n", chunkLines);
        long endByte = byteOffset + encoding.GetByteCount(chunkText);

        yield return new ChunkResult(
            Text: chunkText,
            StartLine: lineNumber,
            EndLine: lineNumber + chunkLines.Count - 1,
            StartByteOffset: startByte,
            EndByteOffset: endByte
        );

        byteOffset = endByte + encoding.GetByteCount("\n");
        lineNumber += chunkLines.Count;
    }
}
```

## Task 5: Update DocumentChunk Entity

**File**: `src/Koan.Context/Models/DocumentChunk.cs`

Add byte offset properties:

```csharp
public class DocumentChunk : Entity<DocumentChunk>
{
    // Existing properties...

    // NEW: Byte-level positioning
    public long StartByteOffset { get; set; }
    public long EndByteOffset { get; set; }

    // NEW: Source URL (computed or stored)
    public string? SourceUrl { get; set; }
}
```

Update `IndexingService` to populate these fields when creating chunks.

## Task 6: PathContext Resolution in ProjectResolver

**File**: `src/Koan.Context/Services/ProjectResolver.cs`

Add method:

```csharp
public async Task<Project> ResolveProjectByPathAsync(
    string pathContext,
    CancellationToken ct = default)
{
    // 1. Normalize path
    string normalizedPath = Path.GetFullPath(pathContext);

    // 2. Find git root
    string? gitRoot = FindGitRoot(normalizedPath);
    if (gitRoot == null)
    {
        gitRoot = Path.GetDirectoryName(normalizedPath)
                  ?? throw new ArgumentException($"Invalid path: {pathContext}");
    }

    // 3. Query existing projects
    var existingProjects = await Project.Query(
        p => p.RootPath == gitRoot,
        ct);

    var project = existingProjects.FirstOrDefault();

    // 4. Auto-create if enabled
    if (project == null && _options.AutoCreateProjectOnQuery)
    {
        project = new Project
        {
            Id = Guid.NewGuid().ToString(),
            Name = ExtractProjectName(gitRoot),
            RootPath = gitRoot,
            ProjectType = ProjectType.Git,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await project.SaveAsync(ct);

        // Trigger background indexing
        if (_options.AutoIndexInBackground)
        {
            _backgroundIndexQueue.Enqueue(project.Id);
        }
    }

    return project ?? throw new ProjectNotFoundException(gitRoot);
}

private string? FindGitRoot(string path)
{
    string? current = path;
    while (current != null)
    {
        if (Directory.Exists(Path.Combine(current, ".git")))
            return current;
        current = Path.GetDirectoryName(current);
    }
    return null;
}

private string ExtractProjectName(string gitRoot)
{
    // Try git remote first
    try
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "config --get remote.origin.url",
            WorkingDirectory = gitRoot,
            RedirectStandardOutput = true,
            UseShellExecute = false
        });

        if (process != null)
        {
            var remote = process.StandardOutput.ReadToEnd().Trim();
            var match = Regex.Match(remote, @"/([^/]+?)(?:\.git)?$");
            if (match.Success)
                return match.Groups[1].Value;
        }
    }
    catch { }

    // Fallback: directory name
    return Path.GetFileName(gitRoot) ?? "unknown";
}
```

## Task 7: Continuation Token System

**File**: `src/Koan.Context/Services/ContinuationTokenService.cs`

```csharp
using System.IO.Compression;
using System.Text.Json;

namespace Koan.Context.Services;

internal record PaginationState(
    string ProjectId,
    string Query,
    float[] QueryEmbedding,
    float Alpha,
    int Offset,
    int TokensConsumed,
    DateTime IssuedAt
);

public interface IContinuationTokenService
{
    string GenerateToken(PaginationState state);
    PaginationState ParseToken(string token);
}

public class ContinuationTokenService : IContinuationTokenService
{
    private const int TokenExpirationHours = 1;

    public string GenerateToken(PaginationState state)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(state);
        var compressed = Compress(json);
        return Convert.ToBase64String(compressed);
    }

    public PaginationState ParseToken(string token)
    {
        try
        {
            var compressed = Convert.FromBase64String(token);
            var json = Decompress(compressed);
            var state = JsonSerializer.Deserialize<PaginationState>(json)!;

            // Validate expiration
            if (DateTime.UtcNow - state.IssuedAt > TimeSpan.FromHours(TokenExpirationHours))
                throw new InvalidOperationException("Continuation token expired");

            return state;
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid continuation token", ex);
        }
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        {
            gzip.CopyTo(output);
        }
        return output.ToArray();
    }
}
```

## Task 8: Source URL Generation

**File**: `src/Koan.Context/Services/SourceUrlGenerator.cs`

```csharp
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Koan.Context.Services;

public interface ISourceUrlGenerator
{
    string? GenerateUrl(string projectRootPath, string filePath, string commitSha);
}

public class GitHubUrlGenerator : ISourceUrlGenerator
{
    public string? GenerateUrl(string projectRootPath, string filePath, string commitSha)
    {
        var remote = GetGitRemote(projectRootPath);
        if (remote == null) return null;

        // Parse github.com/owner/repo or git@github.com:owner/repo.git
        var match = Regex.Match(remote,
            @"(?:github\.com[:/]|git@github\.com:)([^/]+)/([^/.]+?)(?:\.git)?$");

        if (!match.Success) return null;

        string owner = match.Groups[1].Value;
        string repo = match.Groups[2].Value;

        return $"https://github.com/{owner}/{repo}/blob/{commitSha}/{filePath}";
    }

    private string? GetGitRemote(string projectRootPath)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "config --get remote.origin.url",
                WorkingDirectory = projectRootPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null) return null;

            process.WaitForExit();
            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch
        {
            return null;
        }
    }
}

public class GitLabUrlGenerator : ISourceUrlGenerator
{
    public string? GenerateUrl(string projectRootPath, string filePath, string commitSha)
    {
        var remote = GetGitRemote(projectRootPath);
        if (remote == null) return null;

        // Parse gitlab.com/owner/repo
        var match = Regex.Match(remote,
            @"(?:gitlab\.com[:/]|git@gitlab\.com:)([^/]+)/([^/.]+?)(?:\.git)?$");

        if (!match.Success) return null;

        string owner = match.Groups[1].Value;
        string repo = match.Groups[2].Value;

        return $"https://gitlab.com/{owner}/{repo}/-/blob/{commitSha}/{filePath}";
    }

    private string? GetGitRemote(string projectRootPath)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "config --get remote.origin.url",
                WorkingDirectory = projectRootPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null) return null;

            process.WaitForExit();
            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch
        {
            return null;
        }
    }
}
```

## Task 9: Update RetrievalService

This is the most complex task. The `RetrievalService.SearchAsync` method needs major refactoring:

**Key Changes:**
1. Parse continuation token or create new search
2. Fetch generous initial result set (3x estimated chunks)
3. Iterate chunks, accumulating tokens until budget exhausted
4. Deduplicate sources (build SourceFile list)
5. Generate reasoning traces (semantic/keyword scores)
6. Build aggregated insights (topic clustering, completeness)
7. Generate continuation token if more results available
8. Return enhanced SearchResult

**Pseudo-code structure:**

```csharp
public async Task<SearchResult> SearchAsync(...)
{
    var stopwatch = Stopwatch.StartNew();
    options ??= new SearchOptions();

    // Clamp token budget
    int maxTokens = Math.Clamp(options.MaxTokens, 1000, 10000);

    // Parse or create pagination state
    PaginationState? state = null;
    if (options.ContinuationToken != null)
    {
        state = _continuationTokenService.ParseToken(options.ContinuationToken);
        // Reuse query embedding from state
    }
    else
    {
        // Generate new query embedding
        var queryEmbedding = await _embedding.EmbedAsync(query, ct);
        state = new PaginationState(
            ProjectId: projectId,
            Query: query,
            QueryEmbedding: queryEmbedding,
            Alpha: options.Alpha,
            Offset: 0,
            TokensConsumed: 0,
            IssuedAt: DateTime.UtcNow
        );
    }

    // Fetch generous result set
    int estimatedChunks = maxTokens / 200;  // ~200 tokens/chunk
    int fetchCount = Math.Max(estimatedChunks * 3, 50);

    using (EntityContext.Partition($"proj-{Guid.Parse(projectId):N}"))
    {
        var searchResult = await Vector<DocumentChunk>.Search(
            vector: state.QueryEmbedding,
            text: state.Query,
            alpha: state.Alpha,
            topK: fetchCount,
            ct: ct);

        // Token-based pagination
        var chunks = new List<SearchResultChunk>();
        var sourceMap = new Dictionary<string, SourceFile>();
        int tokensAccumulated = 0;
        int chunkIndex = 0;

        foreach (var match in searchResult.Matches.Skip(state.Offset))
        {
            var chunk = await DocumentChunk.Get(match.Id, ct);
            if (chunk == null) continue;

            int chunkTokens = _tokenCounter.EstimateTokens(chunk.SearchText);

            // Check budget
            if (tokensAccumulated + chunkTokens > maxTokens && chunks.Count > 0)
            {
                // Generate continuation token
                var nextState = state with
                {
                    Offset = state.Offset + chunks.Count,
                    TokensConsumed = state.TokensConsumed + tokensAccumulated
                };

                string continuationToken = _continuationTokenService.GenerateToken(nextState);

                return BuildResult(
                    chunks,
                    sourceMap.Values.ToList(),
                    tokensAccumulated,
                    maxTokens,
                    continuationToken,
                    stopwatch.Elapsed,
                    options);
            }

            // Add chunk
            var sourceIndex = AddOrGetSource(sourceMap, chunk);

            chunks.Add(new SearchResultChunk(
                Id: $"doc-{chunkIndex}-chunk-0",
                Text: chunk.SearchText,
                Score: (float)match.Score,
                Provenance: new ChunkProvenance(
                    SourceIndex: sourceIndex,
                    StartByteOffset: chunk.StartByteOffset,
                    EndByteOffset: chunk.EndByteOffset,
                    StartLine: chunk.ChunkRange.StartLine,
                    EndLine: chunk.ChunkRange.EndLine,
                    Language: chunk.Language
                ),
                Reasoning: options.IncludeReasoning ? new RetrievalReasoning(
                    SemanticScore: match.VectorScore,
                    KeywordScore: match.BM25Score,
                    Strategy: DetermineStrategy(options.Alpha)
                ) : null
            ));

            tokensAccumulated += chunkTokens;
            chunkIndex++;
        }

        // No more results
        return BuildResult(
            chunks,
            sourceMap.Values.ToList(),
            tokensAccumulated,
            maxTokens,
            continuationToken: null,
            stopwatch.Elapsed,
            options);
    }
}

private SearchResult BuildResult(
    List<SearchResultChunk> chunks,
    List<SourceFile> sources,
    int tokensReturned,
    int tokensRequested,
    string? continuationToken,
    TimeSpan duration,
    SearchOptions options)
{
    return new SearchResult(
        Chunks: chunks,
        Metadata: new SearchMetadata(
            TokensRequested: tokensRequested,
            TokensReturned: tokensReturned,
            Page: CalculatePage(continuationToken),
            Model: "all-minilm",
            VectorProvider: "weaviate",
            Timestamp: DateTime.UtcNow,
            Duration: duration
        ),
        Sources: new SearchSources(
            TotalFiles: sources.Count,
            Files: sources
        ),
        Insights: options.IncludeInsights ? BuildInsights(chunks) : null,
        ContinuationToken: continuationToken,
        Warnings: new List<string>()
    );
}

private SearchInsights BuildInsights(List<SearchResultChunk> chunks)
{
    // Topic clustering (simple keyword extraction)
    var topics = new Dictionary<string, int>();
    foreach (var chunk in chunks)
    {
        var keywords = ExtractKeywords(chunk.Text);
        foreach (var keyword in keywords)
        {
            topics[keyword] = topics.GetValueOrDefault(keyword) + 1;
        }
    }

    // Completeness assessment
    string completeness = chunks.Count >= 10 ? "comprehensive" :
                         chunks.Count >= 5 ? "partial" : "insufficient";

    return new SearchInsights(
        Topics: topics,
        CompletenessLevel: completeness,
        MissingTopics: null  // Future: use semantic similarity to suggest
    );
}
```

## Task 10: Update SearchController

**File**: `src/Koan.Context/Controllers/SearchController.cs`

```csharp
public record SearchRequest(
    string? ProjectId,
    string? PathContext,    // NEW
    string? LibraryId,
    string Query,
    int? MaxTokens = null,  // NEW
    float? Alpha = null,
    string? ContinuationToken = null,  // NEW
    bool IncludeInsights = true,
    bool IncludeReasoning = true
);

[HttpPost]
public async Task<IActionResult> Search(
    [FromBody] SearchRequest request,
    CancellationToken cancellationToken)
{
    // Priority resolution: ProjectId > PathContext > LibraryId
    string projectId;

    if (!string.IsNullOrEmpty(request.ProjectId))
    {
        projectId = request.ProjectId;
    }
    else if (!string.IsNullOrEmpty(request.PathContext))
    {
        var project = await _projectResolver.ResolveProjectByPathAsync(
            request.PathContext,
            cancellationToken);
        projectId = project.Id;
    }
    else if (!string.IsNullOrEmpty(request.LibraryId))
    {
        var project = await _projectResolver.ResolveProjectAsync(
            libraryId: request.LibraryId,
            cancellationToken: cancellationToken);
        projectId = project.Id;
    }
    else
    {
        return BadRequest(new { error = "Must provide projectId, pathContext, or libraryId" });
    }

    var result = await _retrieval.SearchAsync(
        projectId,
        request.Query,
        new SearchOptions(
            MaxTokens: request.MaxTokens ?? 5000,
            Alpha: request.Alpha ?? 0.7f,
            ContinuationToken: request.ContinuationToken,
            IncludeInsights: request.IncludeInsights,
            IncludeReasoning: request.IncludeReasoning
        ),
        cancellationToken);

    return Ok(result);
}
```

## Task 11: Update MCP Tools

**File**: `src/Koan.Context/Controllers/McpToolsController.cs`

```csharp
[McpTool(
    Name = "context.get_library_docs",
    Description = "Search indexed documentation with auto-project resolution and token budgeting")]
public async Task<SearchResult> GetLibraryDocs(
    [McpParameter(Description = "Library identifier (e.g., /vercel/next.js@14.0)")]
    string? libraryId = null,

    [McpParameter(Description = "File path context for auto-resolution")]
    string? pathContext = null,

    [McpParameter(Description = "Search query/topic")]
    string topic = "",

    [McpParameter(Description = "Token budget (default: 5000, range: 1000-10000)")]
    int tokens = 5000,

    [McpParameter(Description = "Continuation token for pagination")]
    string? continuationToken = null,

    CancellationToken ct = default)
{
    // Resolution logic
    string projectId;

    if (!string.IsNullOrEmpty(pathContext))
    {
        var project = await _projectResolver.ResolveProjectByPathAsync(pathContext, ct);
        projectId = project.Id;
    }
    else if (!string.IsNullOrEmpty(libraryId))
    {
        var project = await _projectResolver.ResolveProjectAsync(libraryId: libraryId, ct: ct);
        projectId = project.Id;
    }
    else
    {
        throw new ArgumentException("Must provide either libraryId or pathContext");
    }

    return await _retrieval.SearchAsync(
        projectId,
        topic,
        new SearchOptions(
            MaxTokens: tokens,
            ContinuationToken: continuationToken
        ),
        ct);
}
```

## Testing Checklist

- [ ] Unit tests for TokenCountingService
- [ ] Unit tests for ContinuationTokenService
- [ ] Integration test: PathContext resolution
- [ ] Integration test: Token budget pagination
- [ ] Integration test: Continuation token flow
- [ ] Integration test: Source URL generation
- [ ] Integration test: Topic clustering in insights
- [ ] Load test: 10k token budget with large result set
- [ ] MCP tool integration test

## Migration Notes

### Database Schema Changes

DocumentChunk needs new columns:
```sql
ALTER TABLE DocumentChunk ADD COLUMN StartByteOffset INTEGER DEFAULT 0;
ALTER TABLE DocumentChunk ADD COLUMN EndByteOffset INTEGER DEFAULT 0;
ALTER TABLE DocumentChunk ADD COLUMN SourceUrl TEXT NULL;
```

### Backward Compatibility

Old SearchController endpoints remain functional. New features are opt-in via:
- `MaxTokens` parameter (defaults to old behavior if not specified)
- `IncludeInsights=false` to skip insights
- `IncludeReasoning=false` to skip reasoning

## Performance Targets

- Search latency: <200ms p95 (same as current)
- Token counting: <1ms per chunk
- Continuation token: <5ms generate/parse
- Source URL generation: <10ms (cached git remote)

## Next Steps

1. Implement remaining tasks in order (3-11)
2. Run unit tests after each task
3. Integration test after tasks 6, 7, 9
4. Full QA test with MCP client
5. Performance benchmark
6. Update documentation
7. Commit and merge to dev branch
