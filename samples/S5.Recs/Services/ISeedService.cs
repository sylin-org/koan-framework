using S5.Recs.Models;

namespace S5.Recs.Services;

public interface ISeedService
{
    Task<string> StartAsync(string source, int limit, bool overwrite, CancellationToken ct);
    Task<string> StartVectorUpsertAsync(IEnumerable<AnimeDoc> items, CancellationToken ct);
    Task<object> GetStatusAsync(string jobId, CancellationToken ct);
    Task<(int anime, int contentPieces, int vectors)> GetStatsAsync(CancellationToken ct);
    Task<int> RebuildTagCatalogAsync(CancellationToken ct);
    Task<int> RebuildGenreCatalogAsync(CancellationToken ct);
}