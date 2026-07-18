using System.Net.Http.Json;
using System.Text.Json;
using Koan.AI.Contracts.Sources;
using Koan.AI.Connector.ZenGarden.Infrastructure;
using Koan.AI.Providers;
using Koan.Core.Logging;
using Koan.ZenGarden;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Connector.ZenGarden.Initialization;

/// <summary>Activates the layered Zen Garden AI provider only when its functional engine resolves an orchestrator.</summary>
internal sealed class ZenGardenAiAdapterContributor : IAiProviderActivator
{
    public async ValueTask<AiProviderActivation?> Activate(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var sources = services.GetRequiredService<IAiSourceRegistry>();
        var logger = services.GetRequiredService<ILogger<ZenGardenAiAdapterContributor>>();

        if (sources.TryGetSource(Constants.Adapter.Id, out var existing))
        {
            ValidateExistingSource(existing!);
            var endpoint = FirstEndpoint(existing!)!;
            var capabilities = SourceCapabilities(existing!);
            if (capabilities.Count == 0)
            {
                capabilities = await ReadCapabilities(endpoint, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "AI source 'zen-garden' is explicit, but its endpoint did not publish a usable capability catalog.");
            }

            return Configure(services, endpoint, capabilities);
        }

        var zenGarden = services.GetService<IZenGardenInitializationProvider>();
        if (zenGarden is null)
        {
            KoanLog.BootDebug(logger, Constants.Logging.Discovery, "inactive",
                ("reason", "zen-garden-engine-unavailable"));
            return null;
        }

        var intent = ZenGardenConnectionIntent.ForOffering(Constants.Discovery.OfferingName);
        var resolution = await zenGarden.Resolve(intent, cancellationToken).ConfigureAwait(false);
        var resolvedEndpoint = resolution?.GetUri("http", "https");
        if (string.IsNullOrWhiteSpace(resolvedEndpoint))
        {
            KoanLog.BootDebug(logger, Constants.Logging.Discovery, "inactive",
                ("reason", "offering-unavailable"));
            return null;
        }

        var discoveredCapabilities = await ReadCapabilities(resolvedEndpoint, cancellationToken).ConfigureAwait(false);
        if (discoveredCapabilities is null || discoveredCapabilities.Count == 0)
        {
            KoanLog.BootWarning(logger, Constants.Logging.Discovery, "inactive",
                ("reason", "capability-catalog-unavailable"));
            return null;
        }

        var source = AiProviderSources.Create(
            Constants.Adapter.Id,
            [resolvedEndpoint],
            discoveredCapabilities.ToDictionary(
                static capability => capability,
                static _ => new AiCapabilityConfig { Model = string.Empty },
                StringComparer.OrdinalIgnoreCase),
            "zen-garden",
            isAutoDiscovered: true);

        var activation = Configure(services, resolvedEndpoint, discoveredCapabilities);
        KoanLog.BootInfo(logger, Constants.Logging.Discovery, "ready",
            ("capabilities", discoveredCapabilities.Count));
        return activation with { Sources = [source] };
    }

    private static AiProviderActivation Configure(
        IServiceProvider services,
        string endpoint,
        IReadOnlySet<string> capabilities)
    {
        services.GetRequiredService<ZenGardenAiRuntime>().Configure(endpoint, capabilities);
        return new AiProviderActivation
        {
            Adapter = services.GetRequiredService<ZenGardenAiAdapter>()
        };
    }

    private static void ValidateExistingSource(AiSourceDefinition source)
    {
        if (!string.Equals(source.Provider, Constants.Adapter.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AI source '{Constants.Adapter.Id}' is reserved for provider '{Constants.Adapter.Id}', " +
                $"but names provider '{source.Provider}'.");
        }

        if (FirstEndpoint(source) is null)
        {
            throw new InvalidOperationException(
                "AI source 'zen-garden' is explicit but has no HTTP endpoint member.");
        }
    }

    private static string? FirstEndpoint(AiSourceDefinition source) =>
        source.Members
            .OrderBy(static member => member.Order)
            .Select(static member => member.ConnectionString)
            .FirstOrDefault(static endpoint =>
                Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https");

    private static HashSet<string> SourceCapabilities(AiSourceDefinition source) =>
        source.Capabilities.Keys
            .Concat(source.Members.SelectMany(static member => member.Capabilities?.Keys ?? []))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static async Task<HashSet<string>?> ReadCapabilities(
        string endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient
            {
                BaseAddress = new Uri(endpoint),
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var health = await http.GetAsync(Constants.Endpoints.Health, cancellationToken)
                .ConfigureAwait(false);
            if (!health.IsSuccessStatusCode) return null;

            var payload = await http.GetFromJsonAsync<JsonElement>(
                Constants.Endpoints.Capabilities,
                cancellationToken).ConfigureAwait(false);
            if (payload.ValueKind != JsonValueKind.Array) return null;

            return payload.EnumerateArray()
                .Select(static item => item.GetString())
                .Where(static capability => !string.IsNullOrWhiteSpace(capability))
                .Select(static capability => capability!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
