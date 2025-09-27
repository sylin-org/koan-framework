using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using S13.DocMind.Models;

namespace S13.DocMind.Infrastructure.Repositories;

public static class SourceDocumentRepository
{
    public static Task<SourceDocument?> GetAsync(Guid id, CancellationToken cancellationToken)
        => SourceDocument.Get(id.ToString(), cancellationToken);

    public static async Task<SourceDocument?> FindByHashAsync(string sha512, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sha512))
        {
            return null;
        }

        var query = await SourceDocument.Query($"Sha512 == '{sha512}'", cancellationToken).ConfigureAwait(false);
        return query.FirstOrDefault();
    }

    public static async Task<IDictionary<Guid, SourceDocument>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var idArray = ids.Distinct().ToArray();
        if (idArray.Length == 0)
        {
            return new Dictionary<Guid, SourceDocument>();
        }

        var filter = string.Join(" || ", idArray.Select(id => $"Id == '{id}'"));
        var results = await SourceDocument.Query(filter, cancellationToken).ConfigureAwait(false);

        var map = new Dictionary<Guid, SourceDocument>(idArray.Length);
        foreach (var entity in results)
        {
            if (Guid.TryParse(entity.Id, out var parsed))
            {
                map[parsed] = entity;
            }
        }

        return map;
    }

    public static async Task<IReadOnlyList<SourceDocument>> GetPendingAsync(CancellationToken cancellationToken)
    {
        var results = await SourceDocument.Query(
            $"Status == {(int)DocumentProcessingStatus.Uploaded} || Status == {(int)DocumentProcessingStatus.Queued}",
            cancellationToken).ConfigureAwait(false);

        return results.ToList();
    }

    public static async Task<bool> HasChangesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var threshold = since.UtcDateTime.ToString("O");
        var filter =
            $"UploadedAt > '{threshold}' || (LastProcessedAt != null && LastProcessedAt > '{threshold}')";
        var results = await SourceDocument.Query(filter, cancellationToken).ConfigureAwait(false);
        return results.Any();
    }
}
