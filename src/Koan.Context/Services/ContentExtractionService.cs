using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Markdown content extraction service
/// </summary>
/// <remarks>
/// Extracts headings, paragraphs, code blocks, and other structured elements
/// from markdown files for indexing.
/// Handles edge cases: unclosed code blocks, mixed line endings, full title hierarchy.
/// </remarks>
public class ContentExtractionService : IContentExtractionService
{
    private readonly ILogger<ContentExtractionService> _logger;

    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex CodeFenceRegex = new(@"^```(\w+)?\s*$", RegexOptions.Compiled);

    // Maximum file size: 50MB
    private const long MaxFileSizeBytes = 50 * 1024 * 1024;

    public ContentExtractionService(ILogger<ContentExtractionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        // Security: Check file size before reading
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"File too large ({fileInfo.Length / (1024.0 * 1024.0):F2} MB). " +
                $"Maximum size is {MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        // Check for empty file
        if (fileInfo.Length == 0)
        {
            _logger.LogWarning("Empty file: {FilePath}", filePath);
            return new ExtractedDocument(
                FilePath: filePath,
                RelativePath: Path.GetFileName(filePath),
                FullText: string.Empty,
                Sections: Array.Empty<ContentSection>(),
                TitleHierarchy: Array.Empty<string>());
        }

        var fullText = await File.ReadAllTextAsync(filePath, cancellationToken);

        if (string.IsNullOrWhiteSpace(fullText))
        {
            _logger.LogWarning("File contains only whitespace: {FilePath}", filePath);
            return new ExtractedDocument(
                FilePath: filePath,
                RelativePath: Path.GetFileName(filePath),
                FullText: fullText,
                Sections: Array.Empty<ContentSection>(),
                TitleHierarchy: Array.Empty<string>());
        }

        var sections = new List<ContentSection>();
        var titleHierarchy = new List<string>();

        // Extract sections
        ExtractSections(fullText, sections, titleHierarchy);

        _logger.LogDebug(
            "Extracted {SectionCount} sections from {FilePath}",
            sections.Count,
            Path.GetFileName(filePath));

        return new ExtractedDocument(
            FilePath: filePath,
            RelativePath: Path.GetFileName(filePath),
            FullText: fullText,
            Sections: sections,
            TitleHierarchy: titleHierarchy);
    }

    private void ExtractSections(string text, List<ContentSection> sections, List<string> titleHierarchy)
    {
        // Normalize line endings: handle both \r\n (Windows) and \n (Unix)
        var normalizedText = text.Replace("\r\n", "\n");
        var lines = normalizedText.Split('\n');

        var currentOffset = 0;
        var inCodeBlock = false;
        var codeBlockLanguage = (string?)null;
        var codeBlockStart = 0;
        var codeBlockLines = new List<string>();
        var hierarchyStack = new Stack<(int Level, string Title)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineStart = currentOffset;
            var lineLength = line.Length + 1; // +1 for newline

            // Check for code fence
            var codeFenceMatch = CodeFenceRegex.Match(line);
            if (codeFenceMatch.Success)
            {
                if (!inCodeBlock)
                {
                    // Start of code block
                    inCodeBlock = true;
                    codeBlockLanguage = codeFenceMatch.Groups[1].Success ? codeFenceMatch.Groups[1].Value : null;
                    codeBlockStart = lineStart;
                    codeBlockLines.Clear();
                }
                else
                {
                    // End of code block
                    var codeText = string.Join("\n", codeBlockLines);
                    sections.Add(new ContentSection(
                        Type: ContentType.CodeBlock,
                        Text: codeText,
                        StartOffset: codeBlockStart,
                        EndOffset: currentOffset + lineLength,
                        Language: codeBlockLanguage));

                    inCodeBlock = false;
                    codeBlockLanguage = null;
                    codeBlockLines.Clear();
                }

                currentOffset += lineLength;
                continue;
            }

            if (inCodeBlock)
            {
                codeBlockLines.Add(line);
                currentOffset += lineLength;
                continue;
            }

            // Check for heading
            var headingMatch = HeadingRegex.Match(line);
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var title = headingMatch.Groups[2].Value.Trim();

                sections.Add(new ContentSection(
                    Type: ContentType.Heading,
                    Text: title,
                    StartOffset: lineStart,
                    EndOffset: currentOffset + lineLength,
                    HeadingLevel: level,
                    Title: title));

                // Update title hierarchy using stack
                UpdateTitleHierarchy(hierarchyStack, level, title);
                titleHierarchy.Clear();
                titleHierarchy.AddRange(hierarchyStack.Reverse().Select(h => h.Title));

                currentOffset += lineLength;
                continue;
            }

            // Check for paragraph (non-empty line)
            if (!string.IsNullOrWhiteSpace(line))
            {
                // Accumulate consecutive lines as a paragraph
                var paragraphStart = lineStart;
                var paragraphLines = new List<string> { line };
                currentOffset += lineLength;

                // Look ahead for more paragraph lines
                while (i + 1 < lines.Length)
                {
                    var nextLine = lines[i + 1];

                    // Stop if: empty line, heading, or code fence
                    if (string.IsNullOrWhiteSpace(nextLine) ||
                        HeadingRegex.IsMatch(nextLine) ||
                        CodeFenceRegex.IsMatch(nextLine))
                    {
                        break;
                    }

                    i++;
                    paragraphLines.Add(nextLine);
                    currentOffset += nextLine.Length + 1;
                }

                var paragraphText = string.Join("\n", paragraphLines);
                sections.Add(new ContentSection(
                    Type: ContentType.Paragraph,
                    Text: paragraphText,
                    StartOffset: paragraphStart,
                    EndOffset: currentOffset));

                continue;
            }

            // Empty line - just advance offset
            currentOffset += lineLength;
        }

        // Handle unclosed code block at end of file
        if (inCodeBlock && codeBlockLines.Count > 0)
        {
            _logger.LogWarning("Unclosed code block detected, emitting accumulated content ({LineCount} lines)", codeBlockLines.Count);

            var codeText = string.Join("\n", codeBlockLines);
            sections.Add(new ContentSection(
                Type: ContentType.CodeBlock,
                Text: codeText,
                StartOffset: codeBlockStart,
                EndOffset: currentOffset,
                Language: codeBlockLanguage));
        }
    }

    /// <summary>
    /// Updates the title hierarchy stack based on heading level
    /// </summary>
    private static void UpdateTitleHierarchy(Stack<(int Level, string Title)> stack, int level, string title)
    {
        // Pop all headings at same or deeper level
        while (stack.Count > 0 && stack.Peek().Level >= level)
        {
            stack.Pop();
        }

        // Push the new heading
        stack.Push((level, title));
    }
}
