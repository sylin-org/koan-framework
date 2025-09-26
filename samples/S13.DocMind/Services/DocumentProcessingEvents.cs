using Microsoft.Extensions.Logging;

namespace S13.DocMind.Services;

public record DocumentProcessingEvent(
    string DocumentId,
    string Stage,
    string Status,
    string TraceId,
    string CorrelationId,
    int Attempt,
    IDictionary<string, object>? Data = null,
    Exception? Exception = null,
    DateTimeOffset? Timestamp = null)
{
    public DateTimeOffset OccurredAt => Timestamp ?? DateTimeOffset.UtcNow;
}

public interface IDocumentProcessingEventSink
{
    void Record(DocumentProcessingEvent evt);
}

public sealed class DocumentProcessingEventLogger : IDocumentProcessingEventSink
{
    private readonly ILogger<DocumentProcessingEventLogger> _logger;

    public DocumentProcessingEventLogger(ILogger<DocumentProcessingEventLogger> logger)
    {
        _logger = logger;
    }

    public void Record(DocumentProcessingEvent evt)
    {
        var level = evt.Exception is null ? LogLevel.Information : LogLevel.Error;
        if (_logger.IsEnabled(level))
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["docId"] = evt.DocumentId,
                ["stage"] = evt.Stage,
                ["status"] = evt.Status,
                ["traceId"] = evt.TraceId,
                ["correlationId"] = evt.CorrelationId,
                ["attempt"] = evt.Attempt
            }))
            {
                _logger.Log(level,
                    evt.Exception,
                    "Document {DocumentId} {Stage} stage recorded {Status}",
                    evt.DocumentId,
                    evt.Stage,
                    evt.Status);
            }
        }
    }
}
