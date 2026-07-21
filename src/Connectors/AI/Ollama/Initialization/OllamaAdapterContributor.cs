using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Sources;
using Koan.AI.Connector.Ollama.Infrastructure;
using Koan.AI.Connector.Ollama.Options;
using Koan.AI.Providers;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.ZenGarden;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.AI.Connector.Ollama.Initialization;

internal sealed class OllamaAdapterContributor : IAiProviderActivator
{
    public async ValueTask<AiProviderActivation?> Activate(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var sourceRegistry = services.GetRequiredService<IAiSourceRegistry>();
        var adapter = services.GetRequiredService<OllamaAdapter>();
        var options = services.GetRequiredService<IOptionsMonitor<OllamaOptions>>().CurrentValue;
        var aiOptions = services.GetRequiredService<IOptions<AiOptions>>().Value;
        var logger = services.GetRequiredService<ILogger<OllamaAdapterContributor>>();

        if (sourceRegistry.TryGetSource(Constants.Adapter.Type, out var existing))
        {
            adapter.SetDefaultEndpoint(FirstEndpoint(existing));
            return new AiProviderActivation { Adapter = adapter };
        }

        var configuredConnection = configuration.GetConnectionString("Ollama");
        var configuredEndpoints = options.Endpoints
            .Where(static endpoint => !string.IsNullOrWhiteSpace(endpoint))
            .ToArray();
        if (!string.IsNullOrWhiteSpace(configuredConnection) && configuredEndpoints.Length > 0)
        {
            throw new InvalidOperationException(
                "Ollama placement is configured twice. Use ConnectionStrings:Ollama for one endpoint or " +
                "Koan:Ai:Ollama:Endpoints for a mesh, not both.");
        }

        string[] endpoints;
        string origin;
        var autoDiscovered = false;

        if (configuredEndpoints.Length > 0)
        {
            endpoints = configuredEndpoints;
            origin = "explicit-config";
        }
        else if (!string.IsNullOrWhiteSpace(configuredConnection))
        {
            endpoints = [await ResolveRequiredConnection(
                services,
                configuredConnection,
                options,
                cancellationToken).ConfigureAwait(false)];
            origin = "explicit-config";
        }
        else if (ShouldDiscover(aiOptions))
        {
            var discovered = await Discover(services, options, cancellationToken).ConfigureAwait(false);
            if (discovered is null)
            {
                KoanLog.BootInfo(logger, LogActions.Discovery, "inactive", ("reason", "no-ready-endpoint"));
                return new AiProviderActivation { Adapter = adapter };
            }

            endpoints = [discovered];
            origin = "auto-discovery";
            autoDiscovered = true;
        }
        else
        {
            KoanLog.BootInfo(logger, LogActions.Discovery, "inactive", ("reason", "auto-discovery-disabled"));
            return new AiProviderActivation { Adapter = adapter };
        }

        var capabilities = Capabilities(options.DefaultModel);
        var source = AiProviderSources.Create(
            Constants.Adapter.Type,
            endpoints,
            capabilities,
            origin,
            autoDiscovered);
        adapter.SetDefaultEndpoint(FirstEndpoint(source));

        KoanLog.BootInfo(logger, LogActions.Discovery, "ready",
            ("members", source.Members.Count),
            ("origin", source.Origin),
            ("model", options.DefaultModel));

        return new AiProviderActivation { Adapter = adapter, Sources = [source] };
    }

    private static async Task<string> ResolveRequiredConnection(
        IServiceProvider services,
        string connection,
        OllamaOptions options,
        CancellationToken cancellationToken)
    {
        if (!ZenGardenConnectionIntent.TryParse(connection, out _)) return connection;

        var coordinator = services.GetRequiredService<IServiceDiscoveryCoordinator>();
        var result = await coordinator.ResolveServiceIntent(
            Constants.Adapter.Type,
            connection,
            DiscoveryContextFor(options),
            cancellationToken).ConfigureAwait(false);
        if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl)) return result.ServiceUrl;

        throw new InvalidOperationException(
            "Ollama explicit Zen Garden intent could not be satisfied. Reference and enable Koan.ZenGarden with " +
            "a ready Ollama offering, choose automatic discovery, or configure a native Ollama HTTP endpoint.");
    }

    private static async Task<string?> Discover(
        IServiceProvider services,
        OllamaOptions options,
        CancellationToken cancellationToken)
    {
        var coordinator = services.GetService<IServiceDiscoveryCoordinator>();
        if (coordinator is null) return null;
        var result = await coordinator.DiscoverService(
            Constants.Adapter.Type,
            DiscoveryContextFor(options),
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccessful ? result.ServiceUrl : null;
    }

    private static DiscoveryContext DiscoveryContextFor(OllamaOptions options) => new()
    {
        OrchestrationMode = KoanEnv.OrchestrationMode,
        HealthCheckTimeout = TimeSpan.FromMilliseconds(750),
        RequiredCapabilities = options.RequiredCapabilities
            .Append(options.DefaultModel)
            .Where(static capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        Parameters = new Dictionary<string, object>
        {
            ["requiredModel"] = options.DefaultModel
        }
    };

    private static IReadOnlyDictionary<string, AiCapabilityConfig> Capabilities(string model) =>
        new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chat"] = new() { Model = model },
            ["Embedding"] = new() { Model = model }
        };

    private static bool ShouldDiscover(AiOptions options) =>
        options.AutoDiscoveryEnabled && (KoanEnv.IsDevelopment || options.AllowDiscoveryInNonDev);

    private static Uri? FirstEndpoint(AiSourceDefinition? source)
    {
        var value = source?.Members
            .OrderBy(static member => member.Order)
            .Select(static member => member.ConnectionString)
            .FirstOrDefault(static endpoint => !string.IsNullOrWhiteSpace(endpoint));
        return Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ? endpoint : null;
    }

    private static class LogActions
    {
        public const string Discovery = "ollama.discovery";
    }
}
