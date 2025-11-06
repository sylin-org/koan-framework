namespace Koan.Context.Services;

/// <summary>
/// Service for extracting structured content from files
/// </summary>
public interface IContentExtractionService
{
    /// <summary>
    /// Extracts structured content from a file
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted document with structured sections</returns>
    Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a document with extracted structured content
/// </summary>
public record ExtractedDocument(
    string FilePath,
    string RelativePath,
    string FullText,
    IReadOnlyList<ContentSection> Sections,
    IReadOnlyList<string> TitleHierarchy);

/// <summary>
/// Represents a section of extracted content
/// </summary>
public record ContentSection(
    ContentType Type,
    string Text,
    int StartOffset,
    int EndOffset,
    string? Language = null,
    int? HeadingLevel = null,
    string? Title = null);

/// <summary>
/// Type of content section
/// </summary>
public enum ContentType
{
    Heading,
    Paragraph,
    CodeBlock,
    ListItem,
    Blockquote
}
