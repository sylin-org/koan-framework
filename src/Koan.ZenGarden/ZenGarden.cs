namespace Koan.ZenGarden;

/// <summary>
/// Static facade for application ergonomics.
/// Configure once with a runtime client, then use Offering/Storage surfaces.
/// </summary>
public static class ZenGarden
{
    private static IZenGardenClient? _client;
    private static readonly object ConfigureLock = new();

    public static ZenGardenOfferingSurface Offering { get; } = new();
    public static ZenGardenStorageSurface Storage { get; } = new();

    public static void Configure(IZenGardenClient client)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        lock (ConfigureLock)
        {
            _client?.Dispose();
            _client = client;
        }
    }

    public static void Configure(
        HttpClient httpClient,
        ILoggerFactory? loggerFactory = null,
        ZenGardenOptions? options = null)
    {
        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        var logger = loggerFactory?.CreateLogger<ZenGardenClient>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ZenGardenClient>.Instance;

        Configure(new ZenGardenClient(httpClient, logger, options));
    }

    internal static IZenGardenClient Client =>
        _client
        ?? (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IZenGardenClient)) as IZenGardenClient)
        ?? throw new InvalidOperationException(
            "IZenGardenClient is not configured. Ensure builder.Services.AddKoan() runs with Koan.ZenGarden referenced, or call AddKoanZenGarden(...), or configure ZenGarden manually.");
}
