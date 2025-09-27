using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using S13.DocMind.Models;

namespace S13.DocMind.Infrastructure.Repositories;

public static class DocumentInsightRepository
{
    public static async Task<bool> HasChangesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var threshold = since.UtcDateTime.ToString("O");
        var filter = $"GeneratedAt > '{threshold}' || (UpdatedAt != null && UpdatedAt > '{threshold}')";
        var results = await DocumentInsight.Query(filter, cancellationToken).ConfigureAwait(false);
        return results.Any();
    }
}
