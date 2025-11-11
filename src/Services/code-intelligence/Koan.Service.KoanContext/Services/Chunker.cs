using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    private const int HeadingMergeTokenThreshold = TargetTokensMin / 4;

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

        var sections = document.Sections.ToList();
        var currentChunk = new StringBuilder();
        var currentTitle = document.TitleHierarchy.Count > 0
            ? string.Join(" > ", document.TitleHierarchy)
            : Path.GetFileNameWithoutExtension(document.FilePath);
        var chunkStartOffset = 0;
        var currentTokens = 0;
        var sectionsInChunk = new List<ContentSection>();
        ChunkedContent? bufferedChunk = null;

        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            cancellationToken.ThrowIfCancellationRequested();

            var isHeading = section.Type == ContentType.Heading;
            var nextIsHeading = index + 1 < sections.Count &&
                                sections[index + 1].Type == ContentType.Heading;

            if (isHeading && currentTokens > 0)
            {
                var chunkBeforeHeading = YieldCurrentChunk(
                    currentChunk,
                    currentTokens,
                    sectionsInChunk,
                    projectId,
                    document.RelativePath,
                    chunkStartOffset,
                    currentTitle);

                var chunkToEmit = ProcessChunkForMerging(chunkBeforeHeading, ref bufferedChunk, out var buffered); 
                if (chunkToEmit != null)
                {
                    yield return chunkToEmit;
                }
                if (!buffered)
                {
                    yield return chunkBeforeHeading;
                }

                ResetChunk(currentChunk, sectionsInChunk, out currentTokens, out chunkStartOffset);
            }

            if (isHeading && section.Title != null)
            {
                currentTitle = section.Title;
            }

            var sectionTokens = EstimateTokens(section.Text);
            var separatorTokens = sectionsInChunk.Count > 0 ? EstimateTokens("\n\n") : 0;

            if (!isHeading && currentTokens > 0)
            {
                while (currentTokens + separatorTokens + sectionTokens > TargetTokensMax)
                {
                    var capacityTokens = TargetTokensMax - currentTokens - separatorTokens;

                    // Try to consume just enough of the section to stay within the hard cap.
                    if (TrySliceSection(section, capacityTokens, out var sliced, out var remainder))
                    {
                        sections[index] = sliced;
                        if (remainder != null)
                        {
                            sections.Insert(index + 1, remainder);
                        }

                        section = sliced;
                        sectionTokens = EstimateTokens(section.Text);
                        separatorTokens = sectionsInChunk.Count > 0 ? EstimateTokens("\n\n") : 0;

                        if (currentTokens + separatorTokens + sectionTokens <= TargetTokensMax)
                        {
                            break;
                        }

                        continue;
                    }

                    var chunk = YieldCurrentChunk(
                        currentChunk,
                        currentTokens,
                        sectionsInChunk,
                        projectId,
                        document.RelativePath,
                        chunkStartOffset,
                        currentTitle);

                    var chunkToEmit = ProcessChunkForMerging(chunk, ref bufferedChunk, out var buffered);
                    if (chunkToEmit != null)
                    {
                        yield return chunkToEmit;
                    }
                    if (!buffered)
                    {
                        yield return chunk;
                    }

                    PrepareNextChunkWithOverlap(
                        currentChunk,
                        sectionsInChunk,
                        out currentTokens,
                        out chunkStartOffset);

                    separatorTokens = sectionsInChunk.Count > 0 ? EstimateTokens("\n\n") : 0;
                }
            }

            // Handle sections larger than max tokens - split them
            if (sectionTokens > TargetTokensMax && !isHeading)
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

                    var chunkToEmit = ProcessChunkForMerging(chunk, ref bufferedChunk, out var buffered);
                    if (chunkToEmit != null)
                    {
                        yield return chunkToEmit;
                    }
                    if (!buffered)
                    {
                        yield return chunk;
                    }

                    // Reset for next chunk with overlap
                    PrepareNextChunkWithOverlap(
                        currentChunk,
                        sectionsInChunk,
                        out currentTokens,
                        out chunkStartOffset);
                }

                if (bufferedChunk != null)
                {
                    yield return bufferedChunk;
                    bufferedChunk = null;
                }

                // Split large section
                await foreach (var largeChunk in SplitLargeSectionAsync(
                    section,
                    projectId,
                    document.RelativePath,
                    currentTitle))
                {
                    yield return largeChunk;
                }

                continue;
            }

            // If adding this section would exceed max, yield current chunk first
            if (!isHeading && currentTokens > 0 && currentTokens + separatorTokens + sectionTokens > TargetTokensMax)
            {
                var chunk = YieldCurrentChunk(
                    currentChunk,
                    currentTokens,
                    sectionsInChunk,
                    projectId,
                    document.RelativePath,
                    chunkStartOffset,
                    currentTitle);

                var chunkToEmit = ProcessChunkForMerging(chunk, ref bufferedChunk, out var buffered);
                if (chunkToEmit != null)
                {
                    yield return chunkToEmit;
                }
                if (!buffered)
                {
                    yield return chunk;
                }

                // Reset for next chunk with overlap
                PrepareNextChunkWithOverlap(
                    currentChunk,
                    sectionsInChunk,
                    out currentTokens,
                    out chunkStartOffset);
            }

            // Add section to current chunk
            AppendSectionToChunk(currentChunk, section, sectionsInChunk, ref currentTokens, ref chunkStartOffset);

            if (!isHeading && currentTokens > TargetTokensMax)
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

                PrepareNextChunkWithOverlap(
                    currentChunk,
                    sectionsInChunk,
                    out currentTokens,
                    out chunkStartOffset);
            }

            // If chunk is big enough and next section is a heading, finalize before heading
            if (!isHeading && currentTokens >= TargetTokensMin && nextIsHeading)
            {
                var chunk = YieldCurrentChunk(
                    currentChunk,
                    currentTokens,
                    sectionsInChunk,
                    projectId,
                    document.RelativePath,
                    chunkStartOffset,
                    currentTitle);

                var chunkToEmit = ProcessChunkForMerging(chunk, ref bufferedChunk, out var buffered);
                if (chunkToEmit != null)
                {
                    yield return chunkToEmit;
                }
                if (!buffered)
                {
                    yield return chunk;
                }

                ResetChunk(
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

            var chunkToEmit = ProcessChunkForMerging(chunk, ref bufferedChunk, out var buffered);
            if (chunkToEmit != null)
            {
                yield return chunkToEmit;
            }
            if (!buffered)
            {
                yield return chunk;
            }
        }

        if (bufferedChunk != null)
        {
            yield return bufferedChunk;
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

        // Fallback: Set language to "markdown" for prose in .md files
        if (language == null && filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            language = "markdown";
        }

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

    private static void ResetChunk(
        StringBuilder currentChunk,
        List<ContentSection> sectionsInChunk,
        out int newTokens,
        out int newStartOffset)
    {
        currentChunk.Clear();
        sectionsInChunk.Clear();
        newTokens = 0;
        newStartOffset = 0;
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

        if (sentences.Count == 0)
        {
            sentences.Add(section.Text);
        }

        if (sentences.Count == 1 && EstimateTokens(sentences[0]) > TargetTokensMax)
        {
            await foreach (var chunk in SplitByTokenWindowAsync(
                sentences[0],
                projectId,
                filePath,
                currentTitle,
                section.Language,
                section.StartOffset))
            {
                yield return chunk;
            }

            yield break;
        }

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

    private async IAsyncEnumerable<ChunkedContent> SplitByTokenWindowAsync(
        string text,
        string projectId,
        string filePath,
        string currentTitle,
        string? language,
        int absoluteStartOffset)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var targetChars = TargetTokensMax * CharsPerToken;
        var index = 0;
        var currentStartOffset = absoluteStartOffset;

        while (index < text.Length)
        {
            var length = Math.Min(targetChars, text.Length - index);
            var slice = text.Substring(index, length);
            var tokenCount = EstimateTokens(slice);
            var endOffset = currentStartOffset + slice.Length;

            yield return new ChunkedContent(
                ProjectId: projectId,
                FilePath: filePath,
                Text: slice.Trim(),
                TokenCount: tokenCount,
                StartOffset: currentStartOffset,
                EndOffset: endOffset,
                Title: currentTitle,
                Language: language);

            if (index + length >= text.Length)
            {
                break;
            }

            var overlapText = GetOverlapText(slice, OverlapTokens);
            var overlapLength = Math.Min(overlapText.Length, length - 1);

            index += length - overlapLength;
            currentStartOffset = endOffset - overlapLength;

            await Task.Yield();
        }
    }

    private static ChunkedContent MergeChunks(ChunkedContent first, ChunkedContent second)
    {
        var combinedText = string.Concat(first.Text, "\n\n", second.Text);
        var combinedTokens = first.TokenCount + second.TokenCount;
        var language = first.Language ?? second.Language;

        return first with
        {
            Text = combinedText,
            TokenCount = combinedTokens,
            EndOffset = second.EndOffset,
            Language = language
        };
    }

    private static bool TrySliceSection(
        ContentSection section,
        int remainingTokenCapacity,
        out ContentSection adjustedSection,
        out ContentSection? remainder)
    {
        adjustedSection = section;
        remainder = null;

        if (remainingTokenCapacity <= 0)
        {
            return false;
        }

        var availableChars = Math.Max(remainingTokenCapacity * CharsPerToken, 0);

        if (availableChars <= 0 || availableChars >= section.Text.Length)
        {
            return false;
        }

        var splitIndex = FindSplitIndex(section.Text, availableChars);

        if (splitIndex <= 0 || splitIndex >= section.Text.Length)
        {
            return false;
        }

        var consumedText = section.Text[..splitIndex];
        var remainingText = section.Text[splitIndex..];

        if (string.IsNullOrWhiteSpace(consumedText) || string.IsNullOrWhiteSpace(remainingText))
        {
            return false;
        }

        var consumedTokens = EstimateTokens(consumedText);
        if (consumedTokens > remainingTokenCapacity)
        {
            while (consumedTokens > remainingTokenCapacity && consumedText.Length > 0)
            {
                consumedText = consumedText[..^1];
                consumedTokens = EstimateTokens(consumedText);
            }

            if (consumedText.Length == 0)
            {
                return false;
            }

            remainingText = section.Text[consumedText.Length..];
        }

        adjustedSection = section with
        {
            Text = consumedText,
            EndOffset = section.StartOffset + consumedText.Length
        };

        remainder = section with
        {
            Text = remainingText,
            StartOffset = adjustedSection.EndOffset
        };

        return true;
    }

    private static int FindSplitIndex(string text, int maxChars)
    {
        var limit = Math.Clamp(maxChars, 1, text.Length - 1);

        for (var i = limit; i > Math.Max(0, limit - 400); i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return limit;
    }

    private static ChunkedContent? ProcessChunkForMerging(
        ChunkedContent chunk,
        ref ChunkedContent? bufferedChunk,
        out bool buffered)
    {
        if (chunk.TokenCount >= HeadingMergeTokenThreshold)
        {
            var bufferedCandidate = bufferedChunk;
            bufferedChunk = null;
            buffered = false;
            return bufferedCandidate;
        }

        if (bufferedChunk is null)
        {
            bufferedChunk = chunk;
            buffered = true;
            return null;
        }

        if (bufferedChunk.TokenCount + chunk.TokenCount <= HeadingMergeTokenThreshold)
        {
            bufferedChunk = MergeChunks(bufferedChunk, chunk);
            buffered = true;
            return null;
        }

        var emit = bufferedChunk;
        bufferedChunk = chunk;
        buffered = true;
        return emit;
    }

    /// <summary>
    /// Splits text into sentences at common boundaries
    /// </summary>
    private static List<string> SplitIntoSentences(string text)
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

        return sentences
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
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

