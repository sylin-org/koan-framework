using S5.Recs.Infrastructure;
using S5.Recs.Models;
using System.Text.Json;

namespace S5.Recs.Providers;

internal sealed class LocalJsonAnimeProvider : IAnimeProvider
{
    public string Code => "local";
    public string Name => "Local JSON";

    public async Task<List<Anime>> FetchAsync(int limit, CancellationToken ct)
    {
        var path = Constants.Paths.OfflineData;
        if (!File.Exists(path)) return [];
        await using var fs = File.OpenRead(path);
        var list = await JsonSerializer.DeserializeAsync<List<Anime>>(fs, cancellationToken: ct) ?? [];
        return list.Take(limit).ToList();
    }
}
