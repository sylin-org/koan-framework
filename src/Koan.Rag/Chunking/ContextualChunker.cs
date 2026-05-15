using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Chunking;

/// <summary>
/// Semantic chunker that produces contextual chunks with parent-child hierarchy.
/// <list type="bullet">
///   <item>Child chunks (200-400 tokens): embedded for precision matching</item>
///   <item>Parent chunks (800-1600 tokens): returned for context preservation</item>
///   <item>Contextual prefixes: LLM-generated document summary prepended to each chunk</item>
/// </list>
/// <para>
/// Follows the Anthropic Contextual Retrieval pattern for prefix generation.
/// The document-level summary is generated once per document and shared across all chunks.
/// </para>
/// </summary>
internal sealed class ContextualChunker
{
    private const double CharsPerToken = 4.0; // Matches EmbeddingMetadata convention

    private readonly ILogger<ContextualChunker> _logger;
    private readonly int _childTokens;
    private readonly int _parentTokens;
    private readonly int _overlapTokens;
    private readonly bool _contextualPrefix;

    public ContextualChunker(IOptions<RagOptions> options, ILogger<ContextualChunker> logger)
    {
        _logger = logger;
        _childTokens = options.Value.ChildChunkTokens;
        _parentTokens = options.Value.ParentChunkTokens;
        _overlapTokens = (int)(_childTokens * 0.15); // 15% overlap
        _contextualPrefix = options.Value.ContextualPrefix;
    }

    /// <summary>
    /// Chunk a document into parent-child pairs with optional contextual prefixes.
    /// </summary>
    public async Task<ChunkedDocument> Chunk(
        string text,
        string? documentTitle,
        string? directive,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ChunkedDocument.Empty;

        // 1. Generate document-level summary for contextual prefixes (once per document)
        var documentSummary = _contextualPrefix
            ? await GenerateDocumentSummary(text, documentTitle, ct)
            : null;

        // 2. Split into sections at heading boundaries
        var sections = SplitIntoSections(text);

        // 3. Create parent chunks from sections
        var parentChunks = CreateParentChunks(sections, documentTitle);

        // 4. Split each parent into child chunks with overlap
        var childChunks = new List<RagContentChunk>();
        foreach (var parent in parentChunks)
        {
            var children = SplitParentIntoChildren(parent, documentSummary);
            childChunks.AddRange(children);
        }

        _logger.LogDebug(
            "Chunked '{Document}': {Parents} parent chunks, {Children} child chunks",
            documentTitle, parentChunks.Count, childChunks.Count);

        return new ChunkedDocument(
            DocumentSummary: documentSummary,
            ParentChunks: parentChunks,
            ChildChunks: childChunks);
    }

    // ── Document Summary ────────────────────────────────────────────────

    private async Task<string?> GenerateDocumentSummary(
        string text, string? documentTitle, CancellationToken ct)
    {
        try
        {
            // Take the first ~2000 tokens for summary generation
            var sample = TruncateToTokens(text, 2000);

            var prompt = documentTitle is not null
                ? $"In 2-3 sentences, summarize what the document titled \"{TextHeuristics.SanitizeForPrompt(documentTitle)}\" is about, based on this excerpt:\n\n{sample}"
                : $"In 2-3 sentences, summarize what this document is about:\n\n{sample}";

            return await Koan.AI.Client.Chat(prompt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to generate document summary for '{Title}'", documentTitle);
            return null;
        }
    }

    // ── Section Splitting ───────────────────────────────────────────────

    private static List<TextSection> SplitIntoSections(string text)
    {
        var sections = new List<TextSection>();
        var lines = text.Split('\n');
        var currentTitle = (string?)null;
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            // Detect headings: lines starting with # (markdown) or all-caps short lines
            if (TextHeuristics.IsHeading(line))
            {
                if (currentContent.Count > 0)
                {
                    sections.Add(new TextSection(currentTitle, string.Join('\n', currentContent)));
                    currentContent.Clear();
                }
                currentTitle = line.TrimStart('#', ' ', '\t');
            }
            else
            {
                currentContent.Add(line);
            }
        }

        if (currentContent.Count > 0)
            sections.Add(new TextSection(currentTitle, string.Join('\n', currentContent)));

        // If no sections found, treat entire text as one section
        if (sections.Count == 0)
            sections.Add(new TextSection(null, text));

        return sections;
    }

    // ── Parent Chunk Creation ───────────────────────────────────────────

    private List<RagContentChunk> CreateParentChunks(
        List<TextSection> sections,
        string? documentTitle)
    {
        var parents = new List<RagContentChunk>();
        var buffer = new List<TextSection>();
        var bufferTokens = 0;

        foreach (var section in sections)
        {
            var sectionTokens = EstimateTokens(section.Content);

            if (sectionTokens > _parentTokens)
            {
                // Flush buffer first
                if (buffer.Count > 0)
                {
                    parents.Add(CreateParentFromBuffer(buffer, documentTitle, parents.Count));
                    buffer.Clear();
                    bufferTokens = 0;
                }

                // Split large section into multiple parents
                var splitParents = SplitLargeSection(section, documentTitle, parents.Count);
                parents.AddRange(splitParents);
            }
            else if (bufferTokens + sectionTokens > _parentTokens)
            {
                // Flush buffer, start new
                parents.Add(CreateParentFromBuffer(buffer, documentTitle, parents.Count));
                buffer.Clear();
                buffer.Add(section);
                bufferTokens = sectionTokens;
            }
            else
            {
                buffer.Add(section);
                bufferTokens += sectionTokens;
            }
        }

        if (buffer.Count > 0)
            parents.Add(CreateParentFromBuffer(buffer, documentTitle, parents.Count));

        return parents;
    }

    private RagContentChunk CreateParentFromBuffer(
        List<TextSection> sections, string? documentTitle, int index)
    {
        var text = string.Join("\n\n", sections.Select(s =>
            s.Title is not null ? $"## {s.Title}\n{s.Content}" : s.Content));

        var sectionTitle = sections.FirstOrDefault(s => s.Title is not null)?.Title;

        return new RagContentChunk(
            Id: $"parent-{index}",
            Text: text,
            TokenCount: EstimateTokens(text),
            DocumentTitle: documentTitle,
            SectionTitle: sectionTitle,
            ParentId: null,
            IsParent: true);
    }

    private List<RagContentChunk> SplitLargeSection(
        TextSection section, string? documentTitle, int startIndex)
    {
        var results = new List<RagContentChunk>();
        var sentences = SplitIntoSentences(section.Content);
        var buffer = new List<string>();
        var bufferTokens = 0;

        foreach (var sentence in sentences)
        {
            var sentenceTokens = EstimateTokens(sentence);

            if (bufferTokens + sentenceTokens > _parentTokens && buffer.Count > 0)
            {
                var text = string.Join(" ", buffer);
                results.Add(new RagContentChunk(
                    Id: $"parent-{startIndex + results.Count}",
                    Text: text,
                    TokenCount: EstimateTokens(text),
                    DocumentTitle: documentTitle,
                    SectionTitle: section.Title,
                    ParentId: null,
                    IsParent: true));

                buffer.Clear();
                bufferTokens = 0;
            }

            buffer.Add(sentence);
            bufferTokens += sentenceTokens;
        }

        if (buffer.Count > 0)
        {
            var text = string.Join(" ", buffer);
            results.Add(new RagContentChunk(
                Id: $"parent-{startIndex + results.Count}",
                Text: text,
                TokenCount: EstimateTokens(text),
                DocumentTitle: documentTitle,
                SectionTitle: section.Title,
                ParentId: null,
                IsParent: true));
        }

        return results;
    }

    // ── Child Chunk Splitting ───────────────────────────────────────────

    private List<RagContentChunk> SplitParentIntoChildren(
        RagContentChunk parent,
        string? documentSummary)
    {
        var children = new List<RagContentChunk>();
        var sentences = SplitIntoSentences(parent.Text);
        var buffer = new List<string>();
        var bufferTokens = 0;
        var childIndex = 0;

        foreach (var sentence in sentences)
        {
            var sentenceTokens = EstimateTokens(sentence);

            if (bufferTokens + sentenceTokens > _childTokens && buffer.Count > 0)
            {
                children.Add(CreateChildChunk(
                    buffer, parent, documentSummary, childIndex++));

                // Overlap: keep last sentence(s) up to overlap budget
                var overlapBuffer = new List<string>();
                var overlapTokens = 0;
                for (var i = buffer.Count - 1; i >= 0; i--)
                {
                    var st = EstimateTokens(buffer[i]);
                    if (overlapTokens + st > _overlapTokens) break;
                    overlapBuffer.Insert(0, buffer[i]);
                    overlapTokens += st;
                }

                buffer.Clear();
                buffer.AddRange(overlapBuffer);
                bufferTokens = overlapTokens;
            }

            buffer.Add(sentence);
            bufferTokens += sentenceTokens;
        }

        if (buffer.Count > 0)
            children.Add(CreateChildChunk(buffer, parent, documentSummary, childIndex));

        return children;
    }

    private static RagContentChunk CreateChildChunk(
        List<string> sentences,
        RagContentChunk parent,
        string? documentSummary,
        int index)
    {
        var rawText = string.Join(" ", sentences);

        // Prepend contextual prefix if available
        var text = documentSummary is not null
            ? $"[Context: {documentSummary}]\n\n{rawText}"
            : rawText;

        return new RagContentChunk(
            Id: $"{parent.Id}-child-{index}",
            Text: text,
            TokenCount: EstimateTokens(text),
            DocumentTitle: parent.DocumentTitle,
            SectionTitle: parent.SectionTitle,
            ParentId: parent.Id,
            IsParent: false);
    }

    // ── Text Utilities ──────────────────────────────────────────────────

    private static List<string> SplitIntoSentences(string text)
    {
        // Split on sentence-ending punctuation followed by whitespace
        var sentences = new List<string>();
        var current = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or '!' or '?' &&
                i + 1 < text.Length &&
                char.IsWhiteSpace(text[i + 1]))
            {
                var sentence = text[current..(i + 1)].Trim();
                if (sentence.Length > 0)
                    sentences.Add(sentence);
                current = i + 1;
            }
        }

        // Remaining text
        if (current < text.Length)
        {
            var remaining = text[current..].Trim();
            if (remaining.Length > 0)
                sentences.Add(remaining);
        }

        return sentences;
    }

    internal static int EstimateTokens(string text)
        => (int)Math.Ceiling(text.Length / CharsPerToken);

    private static string TruncateToTokens(string text, int maxTokens)
    {
        var maxChars = (int)(maxTokens * CharsPerToken);
        return text.Length <= maxChars ? text : text[..maxChars];
    }
}

// ── Supporting Types ────────────────────────────────────────────────────

internal sealed record TextSection(string? Title, string Content);

/// <summary>
/// A content chunk with parent-child relationship and contextual metadata.
/// </summary>
internal sealed record RagContentChunk(
    string Id,
    string Text,
    int TokenCount,
    string? DocumentTitle,
    string? SectionTitle,
    string? ParentId,
    bool IsParent);

/// <summary>
/// Complete chunking result for a single document.
/// </summary>
internal sealed record ChunkedDocument(
    string? DocumentSummary,
    IReadOnlyList<RagContentChunk> ParentChunks,
    IReadOnlyList<RagContentChunk> ChildChunks)
{
    public static ChunkedDocument Empty { get; } = new(null, [], []);
}
