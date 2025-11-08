using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Default chunking service implementation
/// </summary>
/// <remarks>
/// Creates semantic chunks of 800-1000 tokens with 50-token overlap.
/// Respects heading boundaries and logical document structure.
/// Token estimation: approximately 4 characters per token (GPT-style tokenization).
/// QA Issue #10 FIXED: Extracted duplicate logic into YieldChunk method.
/// </remarks>
public class Chunker 
{
    private readonly ILogger<Chunker> _logger;

    private const int TargetTokensMin = 800;
    private const int TargetTokensMax = 1000;
    private const int OverlapTokens = 50;
    private const int CharsPerToken = 4; // Rough estimate for GPT-style tokenization

    public Chunker(ILogger<Chunker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<ChunkedContent> ChunkAsync(
        ExtractedDocument document,
        string projectId,
        string? commitSha = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("Project ID cannot be null or empty", nameof(projectId));
        }

        if (document.Sections.Count == 0)
        {
            _logger.LogDebug("Document {FilePath} has no sections to chunk", document.FilePath);
            yield break;
        }

        var currentChunk = new StringBuilder();
        var currentTitle = document.TitleHierarchy.Count > 0
            ? string.Join(" > ", document.TitleHierarchy)
            : Path.GetFileNameWithoutExtension(document.FilePath);
        var chunkStartOffset = 0;
        var currentTokens = 0;
        var sectionsInChunk = new List<ContentSection>();
        var chunksYielded = 0;

        foreach (var section in document.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Update title if we hit a heading
            if (section.Type == ContentType.Heading && section.Title != null)
            {
                currentTitle = section.Title;
            }

            var sectionTokens = EstimateTokens(section.Text);

            // Handle sections larger than max tokens - split them
            if (sectionTokens > TargetTokensMax)
            {
                // Yield current chunk first if it has content
                if (currentTokens > 0)
                {
                    var chunk = YieldCurrentChunk(
                        currentChunk,
                        currentTokens,
                        sectionsInChunk,
                        projectId,
                        document.RelativePath,
                        chunkStartOffset,
                        currentTitle);

                    yield return chunk;
                    chunksYielded++;

                    // Reset for next chunk with overlap
                    PrepareNextChunkWithOverlap(
                        currentChunk,
                        sectionsInChunk,
                        out currentTokens,
                        out chunkStartOffset);
                }

                // Split large section
                await foreach (var largeChunk in SplitLargeSectionAsync(
                    section,
                    projectId,
                    document.RelativePath,
                    currentTitle))
                {
                    yield return largeChunk;
                    chunksYielded++;
                }

                continue;
            }

            // If adding this section would exceed max, yield current chunk first
            if (currentTokens > 0 && currentTokens + sectionTokens > TargetTokensMax)
            {
                var chunk = YieldCurrentChunk(
                    currentChunk,
                    currentTokens,
                    sectionsInChunk,
                    projectId,
                    document.RelativePath,
                    chunkStartOffset,
                    currentTitle);

                yield return chunk;
                chunksYielded++;

                // Reset for next chunk with overlap
                PrepareNextChunkWithOverlap(
                    currentChunk,
                    sectionsInChunk,
                    out currentTokens,
                    out chunkStartOffset);
            }

            // Add section to current chunk
            AppendSectionToChunk(currentChunk, section, sectionsInChunk, ref currentTokens, ref chunkStartOffset);

            // If chunk is big enough, consider yielding at heading boundaries
            if (currentTokens >= TargetTokensMin && section.Type == ContentType.Heading)
            {
                var chunk = YieldCurrentChunk(
                    currentChunk,
                    currentTokens,
                    sectionsInChunk,
                    projectId,
                    document.RelativePath,
                    chunkStartOffset,
                    currentTitle);

                yield return chunk;
                chunksYielded++;

                // Reset for next chunk with overlap
                PrepareNextChunkWithOverlap(
                    currentChunk,
                    sectionsInChunk,
                    out currentTokens,
                    out chunkStartOffset);
            }

            await Task.Yield(); // Allow cooperative cancellation
        }

        // Yield remaining content
        if (currentTokens > 0)
        {
            var chunk = YieldCurrentChunk(
                currentChunk,
                currentTokens,
                sectionsInChunk,
                projectId,
                document.RelativePath,
                chunkStartOffset,
                currentTitle);

            yield return chunk;
            chunksYielded++;
        }
    }

    /// <summary>
    /// Yields the current chunk as ChunkedContent
    /// QA Issue #10 FIX: Extracted to eliminate duplication
    /// </summary>
    private ChunkedContent YieldCurrentChunk(
        StringBuilder currentChunk,
        int currentTokens,
        List<ContentSection> sectionsInChunk,
        string projectId,
        string filePath,
        int chunkStartOffset,
        string currentTitle)
    {
        var chunkText = currentChunk.ToString().Trim();
        var language = sectionsInChunk.FirstOrDefault(s => s.Language != null)?.Language;
        var endOffset = sectionsInChunk.Count > 0
            ? sectionsInChunk.Last().EndOffset
            : chunkStartOffset + chunkText.Length;

        return new ChunkedContent(
            ProjectId: projectId,
            FilePath: filePath,
            Text: chunkText,
            TokenCount: currentTokens,
            StartOffset: chunkStartOffset,
            EndOffset: endOffset,
            Title: currentTitle,
            Language: language);
    }

    /// <summary>
    /// Prepares the next chunk with overlap from the current chunk
    /// </summary>
    private void PrepareNextChunkWithOverlap(
        StringBuilder currentChunk,
        List<ContentSection> sectionsInChunk,
        out int newTokens,
        out int newStartOffset)
    {
        var chunkText = currentChunk.ToString();
        var overlapText = GetOverlapText(chunkText, OverlapTokens);

        currentChunk.Clear();
        currentChunk.Append(overlapText);
        newTokens = EstimateTokens(overlapText);

        // Calculate new start offset (approximation based on overlap)
        newStartOffset = sectionsInChunk.Count > 0
            ? sectionsInChunk.Last().EndOffset - overlapText.Length
            : 0;

        sectionsInChunk.Clear();
    }

    /// <summary>
    /// Appends a section to the current chunk
    /// </summary>
    private void AppendSectionToChunk(
        StringBuilder currentChunk,
        ContentSection section,
        List<ContentSection> sectionsInChunk,
        ref int currentTokens,
        ref int chunkStartOffset)
    {
        if (currentChunk.Length > 0)
        {
            currentChunk.AppendLine();
            currentChunk.AppendLine();
            currentTokens += EstimateTokens("\n\n"); // QA Issue #20 FIX: Include newlines in token count
        }

        currentChunk.Append(section.Text);
        currentTokens += EstimateTokens(section.Text);
        sectionsInChunk.Add(section);

        if (sectionsInChunk.Count == 1)
        {
            chunkStartOffset = section.StartOffset;
        }
    }

    /// <summary>
    /// Splits a section larger than max tokens at sentence boundaries
    /// QA Issue #19 FIX: Handle massive sections
    /// </summary>
    private async IAsyncEnumerable<ChunkedContent> SplitLargeSectionAsync(
        ContentSection section,
        string projectId,
        string filePath,
        string currentTitle)
    {
        var sentences = SplitIntoSentences(section.Text);
        var currentChunk = new StringBuilder();
        var currentTokens = 0;
        var chunkStartOffset = section.StartOffset;
        var currentOffset = section.StartOffset;

        foreach (var sentence in sentences)
        {
            var sentenceTokens = EstimateTokens(sentence);

            if (currentTokens > 0 && currentTokens + sentenceTokens > TargetTokensMax)
            {
                // Yield current chunk
                yield return new ChunkedContent(
                    ProjectId: projectId,
                    FilePath: filePath,
                    Text: currentChunk.ToString().Trim(),
                    TokenCount: currentTokens,
                    StartOffset: chunkStartOffset,
                    EndOffset: currentOffset,
                    Title: currentTitle,
                    Language: section.Language);

                // Start new chunk with overlap
                var overlapText = GetOverlapText(currentChunk.ToString(), OverlapTokens);
                currentChunk.Clear();
                currentChunk.Append(overlapText);
                currentTokens = EstimateTokens(overlapText);
                chunkStartOffset = currentOffset - overlapText.Length;
            }

            if (currentChunk.Length > 0)
            {
                currentChunk.Append(' ');
            }

            currentChunk.Append(sentence);
            currentTokens += sentenceTokens;
            currentOffset += sentence.Length + 1;

            await Task.Yield();
        }

        // Yield remaining content
        if (currentTokens > 0)
        {
            yield return new ChunkedContent(
                ProjectId: projectId,
                FilePath: filePath,
                Text: currentChunk.ToString().Trim(),
                TokenCount: currentTokens,
                StartOffset: chunkStartOffset,
                EndOffset: currentOffset,
                Title: currentTitle,
                Language: section.Language);
        }
    }

    /// <summary>
    /// Splits text into sentences at common boundaries
    /// </summary>
    private static IEnumerable<string> SplitIntoSentences(string text)
    {
        // Split on sentence boundaries (. ! ?) followed by whitespace
        var sentences = new List<string>();
        var currentSentence = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            currentSentence.Append(text[i]);

            // Check for sentence ending
            if ((text[i] == '.' || text[i] == '!' || text[i] == '?') &&
                i + 1 < text.Length &&
                char.IsWhiteSpace(text[i + 1]))
            {
                sentences.Add(currentSentence.ToString().Trim());
                currentSentence.Clear();
            }
        }

        // Add remaining text
        if (currentSentence.Length > 0)
        {
            sentences.Add(currentSentence.ToString().Trim());
        }

        return sentences.Where(s => !string.IsNullOrWhiteSpace(s));
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Rough estimation: 4 characters per token
        // This can be refined with actual tokenizer later
        return (text.Length + CharsPerToken - 1) / CharsPerToken;
    }

    /// <summary>
    /// Gets overlap text from the end of a chunk
    /// QA Issue #32 FIX: Simplified logic
    /// </summary>
    private static string GetOverlapText(string text, int targetTokens)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var targetChars = targetTokens * CharsPerToken;

        if (text.Length <= targetChars)
            return text;

        // Get last N characters
        var overlapStart = Math.Max(0, text.Length - targetChars - 100);
        var overlap = text.Substring(overlapStart);

        // Find last sentence boundary
        var lastPeriod = overlap.LastIndexOf(". ", StringComparison.Ordinal);
        var lastNewline = overlap.LastIndexOf("\n\n", StringComparison.Ordinal);
        var breakPoint = Math.Max(lastPeriod, lastNewline);

        if (breakPoint > 0 && breakPoint < overlap.Length - 20)
        {
            return overlap.Substring(breakPoint + 2).TrimStart();
        }

        // Fall back to last targetChars
        return text.Substring(Math.Max(0, text.Length - targetChars));
    }
}


/// <summary>
/// Represents a chunk of text ready for embedding and indexing
/// </summary>
public record ChunkedContent(
    string ProjectId,
    string FilePath,
    string Text,
    int TokenCount,
    int StartOffset,
    int EndOffset,
    string? Title = null,
    string? Language = null);

