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
    public static ZenGardenCapabilitySurface Capability { get; } = new();

    /// <summary>
    /// Returns the recommended model for an AI capability using orchestrator recommendations.
    /// Pass <see cref="Koan.Core.AI.AiCapability"/> constants: Chat, Embed, Vision, Ocr, Quick, Synthesis, Thinking, Tools.
    /// Returns null if the orchestrator is unreachable or no recommendations are available.
    /// </summary>
    public static string? RecommendedModel(string capability)
    {
        var advisor = Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(Koan.Core.AI.IAiModelAdvisor))
            as Koan.Core.AI.IAiModelAdvisor;
        return advisor?.GetRecommendedModel(capability);
    }

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

    public static IDisposable On<TEvent>(
        ZenGardenSubscription subscription,
        Func<TEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(handler);

        if (typeof(TEvent) != typeof(ZenGardenAvailabilityEvent))
        {
            throw new NotSupportedException(
                $"Unsupported Zen Garden event type '{typeof(TEvent).Name}'. " +
                $"Use '{nameof(ZenGardenAvailabilityEvent)}' for tool availability subscriptions.");
        }

        return Client.Subscribe(
            subscription,
            (evt, ct) => handler((TEvent)(object)evt, ct),
            options);
    }

    public static IDisposable On<TEvent>(
        ZenGardenCapabilityWish wish,
        Func<TEvent, CancellationToken, ValueTask> handler,
        ZenGardenCapabilityWatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(wish);
        ArgumentNullException.ThrowIfNull(handler);

        if (typeof(TEvent) != typeof(ZenGardenCapabilityProgressEvent))
        {
            throw new NotSupportedException(
                $"Unsupported Zen Garden event type '{typeof(TEvent).Name}'. " +
                $"Use '{nameof(ZenGardenCapabilityProgressEvent)}' for capability progress subscriptions.");
        }

        return Client.SubscribeCapability(
            wish.RequestId,
            (evt, ct) => handler((TEvent)(object)evt, ct),
            options);
    }

    internal static IZenGardenClient Client =>
        _client
        ?? (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IZenGardenClient)) as IZenGardenClient)
        ?? throw new InvalidOperationException(
            "IZenGardenClient is not configured. Ensure builder.Services.AddKoan() runs with Koan.ZenGarden referenced, or call AddKoanZenGarden(...), or configure ZenGarden manually.");
}
