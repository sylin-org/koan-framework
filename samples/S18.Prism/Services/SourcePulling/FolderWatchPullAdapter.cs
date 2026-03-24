using Koan.Data.Core;
using S18.Prism.Models;

namespace S18.Prism.Services.SourcePulling;

/// <summary>
/// Scans a local directory for new/modified files matching a pattern
/// and creates notes from their content.
/// </summary>
public sealed class FolderWatchPullAdapter : ISourcePullAdapter
{
    private readonly ILogger<FolderWatchPullAdapter> _logger;

    public FolderWatchPullAdapter(ILogger<FolderWatchPullAdapter> logger)
    {
        _logger = logger;
    }

    public SourceType SupportedType => SourceType.FolderWatch;

    public async Task<List<Note>> Pull(Source source, CancellationToken ct)
    {
        var config = SourceConfigParser.Parse<FolderWatchConfig>(source.Configuration);

        if (string.IsNullOrWhiteSpace(config.Path))
        {
            _logger.LogWarning("FolderWatch source {SourceId} has no path configured", source.Id);
            return [];
        }

        var fullPath = Path.GetFullPath(config.Path);
        var allowedBases = new[] { Directory.GetCurrentDirectory(), "/app/storage", "/app/data" };
        if (!allowedBases.Any(b => fullPath.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Path {Path} is outside allowed directories", config.Path);
            return [];
        }

        if (!Directory.Exists(fullPath))
        {
            _logger.LogWarning("FolderWatch path does not exist: {Path}", fullPath);
            return [];
        }

        _logger.LogInformation("Scanning folder {Path} with pattern {Pattern}", fullPath, config.Pattern);

        // Resolve glob pattern: support simple patterns like "*.md" or "**/*.md"
        var searchPattern = ExtractSearchPattern(config.Pattern);
        var searchOption = config.Pattern.Contains("**")
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var files = Directory.GetFiles(fullPath, searchPattern, searchOption);

        // Pre-fetch all existing notes for this source to avoid N+1 queries
        var existingNotes = await Note.Query(
            n => n.SourceId == source.Id.ToString(), ct);
        var existingByUrl = existingNotes
            .Where(n => n.SourceUrl is not null)
            .ToDictionary(n => n.SourceUrl!, StringComparer.OrdinalIgnoreCase);

        var notes = new List<Note>();

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var fileInfo = new FileInfo(filePath);

                // Skip files not modified since last pull
                if (source.LastPulledAt is not null &&
                    fileInfo.LastWriteTimeUtc <= source.LastPulledAt.Value)
                    continue;

                // Check for existing note by source URL (use file path as URL)
                var fileUrl = new Uri(filePath).AbsoluteUri;
                if (existingByUrl.TryGetValue(fileUrl, out var existingNote))
                {
                    // If file was modified, update the existing note's content
                    var updatedContent = await File.ReadAllTextAsync(filePath, ct);
                    existingNote.Blocks =
                    [
                        new ContentBlock
                        {
                            Kind = ContentKind.Text,
                            Content = updatedContent,
                            Order = 0,
                            Source = new ContentSource(
                                fileInfo.Name,
                                ResolveMimeType(fileInfo.Extension),
                                Extractor: nameof(FolderWatchPullAdapter))
                        }
                    ];
                    await existingNote.Save(ct);
                    notes.Add(existingNote);

                    _logger.LogDebug("Updated existing note {NoteId} from file: {FilePath}",
                        existingNote.Id, filePath);
                    continue;
                }

                var content = await File.ReadAllTextAsync(filePath, ct);

                var note = new Note
                {
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    SpaceId = source.SpaceId,
                    Origin = NoteOrigin.Source,
                    AutoIngested = true,
                    SourceId = source.Id.ToString(),
                    SourceUrl = fileUrl,
                    SourcePublishedAt = fileInfo.LastWriteTimeUtc,
                    Blocks =
                    [
                        new ContentBlock
                        {
                            Kind = ContentKind.Text,
                            Content = content,
                            Order = 0,
                            Source = new ContentSource(
                                fileInfo.Name,
                                ResolveMimeType(fileInfo.Extension),
                                Extractor: nameof(FolderWatchPullAdapter))
                        }
                    ]
                };

                await note.Save(ct);
                notes.Add(note);

                _logger.LogDebug("Created note {NoteId} from file: {FilePath}", note.Id, filePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to process file {FilePath}", filePath);
            }
        }

        return notes;
    }

    /// <summary>
    /// Extracts a search pattern suitable for Directory.GetFiles from a glob pattern.
    /// "**/*.md" → "*.md", "*.txt" → "*.txt"
    /// </summary>
    private static string ExtractSearchPattern(string pattern)
    {
        // Strip leading **/ or */ prefixes
        var clean = pattern.Replace("**/", "").Replace("*/", "");
        return string.IsNullOrWhiteSpace(clean) ? "*" : clean;
    }

    private static string ResolveMimeType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".md" or ".markdown" => "text/markdown",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html",
            _ => "text/plain"
        };
}
