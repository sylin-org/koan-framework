using System.Runtime.CompilerServices;
using System.Text;
using Koan.AI.Contracts.Models;
using Koan.Rag.Abstractions;
using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Segmentation;

/// <summary>
/// Segments large text documents into overlapping windows at heading boundaries.
/// Each segment carries structural context accumulated from headings encountered so far.
/// <para>
/// Segment size: configurable (default ~150K chars / ~37K tokens).
/// Overlap: 15% of segment size.
/// </para>
/// </summary>
internal sealed class TextDocumentSegmenter : IDocumentSegmenter
{
    private readonly ILogger<TextDocumentSegmenter> _logger;
    private readonly long _segmentSizeChars;
    private readonly long _overlapChars;
    private const long DefaultSegmentSizeChars = 150_000; // ~37K tokens
    private const double OverlapRatio = 0.15;
    private const long LargeDocumentThreshold = 500_000; // ~125K tokens → segment

    public TextDocumentSegmenter(
        IOptions<RagOptions> options,
        ILogger<TextDocumentSegmenter> logger)
    {
        _logger = logger;
        _segmentSizeChars = DefaultSegmentSizeChars;
        _overlapChars = (long)(_segmentSizeChars * OverlapRatio);
    }

    public bool CanSegment(string filePath, long fileSizeBytes)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is not (".txt" or ".md" or ".markdown" or ".html" or ".htm"
            or ".xml" or ".json" or ".yaml" or ".yml" or ".csv" or ".log"))
            return false;

        return fileSizeBytes > LargeDocumentThreshold;
    }

    public async IAsyncEnumerable<DocumentSegment> Segment(
        string filePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Path.IsPathFullyQualified(filePath))
            throw new ArgumentException("File path must be fully qualified", nameof(filePath));

        // Reject UNC/network paths to prevent NTLM relay and network file access
        if (filePath.StartsWith(@"\\") || filePath.StartsWith("//"))
            throw new ArgumentException("Network (UNC) paths are not permitted for file ingestion", nameof(filePath));

        _logger.LogDebug("Segmenting large text document: {File}", filePath);

        using var reader = new StreamReader(filePath, Encoding.UTF8);
        var buffer = new StringBuilder((int)_segmentSizeChars);
        var overlapBuffer = new StringBuilder((int)_overlapChars);
        var segmentIndex = 0;
        var currentHeadings = new List<string>();
        var lastHeading = (string?)null;

        while (reader.Peek() != -1)
        {
            ct.ThrowIfCancellationRequested();

            // Read until we hit the segment size
            while (buffer.Length < _segmentSizeChars && reader.Peek() != -1)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                // Track headings for structural context
                if (TextHeuristics.IsHeading(line))
                {
                    lastHeading = line.TrimStart('#', ' ', '\t');
                    if (!currentHeadings.Contains(lastHeading))
                        currentHeadings.Add(lastHeading);
                }

                buffer.AppendLine(line);
            }

            if (buffer.Length == 0)
                break;

            var text = buffer.ToString();
            var structuralContext = currentHeadings.Count > 0
                ? string.Join(" > ", currentHeadings.TakeLast(3))
                : null;

            yield return new DocumentSegment
            {
                Bytes = Encoding.UTF8.GetBytes(text),
                Modality = Modality.Text,
                SegmentId = $"segment-{segmentIndex}",
                SegmentIndex = segmentIndex,
                SectionTitle = lastHeading,
                StructuralContext = structuralContext
            };

            segmentIndex++;

            // Prepare overlap: keep the last N chars for the next segment
            buffer.Clear();
            if (text.Length > _overlapChars)
            {
                var overlap = text[(text.Length - (int)_overlapChars)..];
                buffer.Append(overlap);
            }
        }

        _logger.LogDebug(
            "Segmented '{File}' into {Count} segments",
            Path.GetFileName(filePath), segmentIndex);
    }

}
