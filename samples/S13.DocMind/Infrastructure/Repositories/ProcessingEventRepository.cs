using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using S13.DocMind.Models;

namespace S13.DocMind.Infrastructure.Repositories;

public static class ProcessingEventRepository
{
    public static async Task<IReadOnlyList<DocumentProcessingEvent>> GetTimelineAsync(
        Guid? documentId,
        DocumentProcessingStage? stage,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var filters = new List<string>();
        if (documentId.HasValue)
        {
            filters.Add($"SourceDocumentId == '{documentId.Value}'");
        }

        if (stage.HasValue)
        {
            filters.Add($"Stage == {(int)stage.Value}");
        }

        var filterExpression = filters.Count == 0 ? null : string.Join(" && ", filters);
        var events = filterExpression is null
            ? await DocumentProcessingEvent.All(cancellationToken).ConfigureAwait(false)
            : await DocumentProcessingEvent.Query(filterExpression, cancellationToken).ConfigureAwait(false);

        if (from.HasValue)
        {
            events = events.Where(evt => evt.CreatedAt >= from.Value).ToList();
        }

        if (to.HasValue)
        {
            events = events.Where(evt => evt.CreatedAt <= to.Value).ToList();
        }

        return events
            .OrderBy(evt => evt.SourceDocumentId)
            .ThenBy(evt => evt.CreatedAt)
            .ToList();
    }
}
