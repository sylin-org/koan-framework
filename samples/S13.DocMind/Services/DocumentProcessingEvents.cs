using Microsoft.Extensions.Logging;
using Koan.Data.Core;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed record DocumentProcessingEventEntry(
    Guid SourceDocumentId,
    DocumentProcessingStage Stage,
    DocumentProcessingStatus Status,
    string? Detail = null,
    string? Error = null,
    IReadOnlyDictionary<string, string>? Context = null,
    IReadOnlyDictionary<string, double>? Metrics = null,
    int Attempt = 1,
    string? CorrelationId = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    TimeSpan? Duration = null,
    Guid? ChunkId = null,
    Guid? InsightId = null,
    bool IsTerminal = false);

public interface IDocumentProcessingEventSink
{
    Task RecordAsync(DocumentProcessingEventEntry entry, CancellationToken cancellationToken);
}

public sealed class DocumentProcessingEventRepositorySink : IDocumentProcessingEventSink
{
    private readonly ILogger<DocumentProcessingEventRepositorySink> _logger;

    public DocumentProcessingEventRepositorySink(ILogger<DocumentProcessingEventRepositorySink> logger)
    {
        _logger = logger;
    }

    public async Task RecordAsync(DocumentProcessingEventEntry entry, CancellationToken cancellationToken)
    {
        var entity = new DocumentProcessingEvent
        {
            SourceDocumentId = entry.SourceDocumentId,
            Stage = entry.Stage,
            Status = entry.Status,
            Detail = entry.Detail,
            Error = entry.Error,
            CreatedAt = DateTimeOffset.UtcNow,
            Attempt = Math.Max(1, entry.Attempt),
            CorrelationId = entry.CorrelationId,
            InputTokens = entry.InputTokens,
            OutputTokens = entry.OutputTokens,
            Duration = entry.Duration,
            ChunkId = entry.ChunkId,
            InsightId = entry.InsightId,
            IsTerminal = entry.IsTerminal
        };

        if (entry.Context is not null)
        {
            foreach (var kvp in entry.Context)
            {
                entity.Context[kvp.Key] = kvp.Value;
            }
        }

        if (entry.Metrics is not null)
        {
            foreach (var kvp in entry.Metrics)
            {
                entity.Metrics[kvp.Key] = kvp.Value;
            }
        }

        await entity.Save(cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Document {DocumentId} recorded stage {Stage} -> {Status} (attempt {Attempt})",
                entry.SourceDocumentId,
                entry.Stage,
                entry.Status,
                entity.Attempt);
        }
    }
}
