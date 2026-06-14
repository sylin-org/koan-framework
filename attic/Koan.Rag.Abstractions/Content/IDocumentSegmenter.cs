using Koan.AI.Contracts.Models;

namespace Koan.Rag.Abstractions;

/// <summary>
/// Segments large documents into streaming chunks for parallel processing.
/// Sits before the content adapter in the ingestion pipeline. Small files
/// bypass segmentation entirely.
/// </summary>
public interface IDocumentSegmenter
{
    /// <summary>Can this segmenter handle the given file?</summary>
    bool CanSegment(string filePath, long fileSizeBytes);

    /// <summary>
    /// Yield document segments as a stream. Each segment flows through
    /// the content adapter and chunker independently.
    /// </summary>
    IAsyncEnumerable<DocumentSegment> Segment(
        string filePath,
        CancellationToken ct = default);
}

/// <summary>
/// A segment of a large document, yielded by <see cref="IDocumentSegmenter"/>.
/// </summary>
public sealed record DocumentSegment
{
    /// <summary>Segment content bytes.</summary>
    public required byte[] Bytes { get; init; }

    /// <summary>Content modality of this segment.</summary>
    public required Modality Modality { get; init; }

    /// <summary>Segment identifier within the document (e.g., "pages-1-50").</summary>
    public required string SegmentId { get; init; }

    /// <summary>Zero-based segment index.</summary>
    public required int SegmentIndex { get; init; }

    /// <summary>Total segments (-1 if streaming/unknown).</summary>
    public int TotalSegments { get; init; } = -1;

    /// <summary>Section title detected from headings.</summary>
    public string? SectionTitle { get; init; }

    /// <summary>
    /// Structural context (e.g., "Chapter 3 of HIPAA Manual, Section: Data Transfer").
    /// Accumulated from headings encountered during segmentation.
    /// </summary>
    public string? StructuralContext { get; init; }
}
