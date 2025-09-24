using Microsoft.Extensions.Logging;
using S5.Recs.Models;

namespace S5.Recs.Providers;

internal sealed class LocalJsonMediaProvider(ILogger<LocalJsonMediaProvider>? logger = null) : IMediaProvider
{
    public string Code => "local";
    public string Name => "Local JSON";

    public MediaType[] SupportedTypes => Array.Empty<MediaType>();

    public Task<List<Media>> FetchAsync(MediaType mediaType, int limit, CancellationToken ct)
    {
        logger?.LogInformation("LocalJsonMediaProvider: No local data available, returning empty list for MediaType '{MediaType}'", mediaType.Name);
        return Task.FromResult(new List<Media>());
    }

    public async IAsyncEnumerable<List<Media>> FetchStreamAsync(MediaType mediaType, int limit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        logger?.LogInformation("LocalJsonMediaProvider: No local data available for streaming MediaType '{MediaType}'", mediaType.Name);
        await Task.Yield();
        yield break;
    }
}