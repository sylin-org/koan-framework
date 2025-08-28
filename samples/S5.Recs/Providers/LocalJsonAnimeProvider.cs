using Newtonsoft.Json;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Providers;

internal sealed class LocalJsonAnimeProvider : IAnimeProvider
{
    public string Code => "local";
    public string Name => "Local JSON";

    public async Task<List<Anime>> FetchAsync(int limit, CancellationToken ct)
    {
        var path = Constants.Paths.OfflineData;
        if (!File.Exists(path)) return [];
    var json = await File.ReadAllTextAsync(path, ct);
    var list = JsonConvert.DeserializeObject<List<Anime>>(json) ?? [];
        return list.Take(limit).ToList();
    }
}
