using System.Net.Http.Json;
using System.Text.Json;
using Koan.AI.Connector.ZenGarden.Infrastructure;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Contracts;
using Koan.Core.Logging;
using Koan.ZenGarden;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.AI.Connector.ZenGarden.Initialization;

/// <summary>
/// Discovers the Zen Garden AI Orchestrator via Zen Garden offering resolution,
/// queries its capability set, and registers the unified adapter.
/// </summary>
internal sealed class ZenGardenAiAdapterContributor : IAiAdapterContributor
{
    public async ValueTask Contribute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var adapterRegistry = services.GetRequiredService<IAiAdapterRegistry>();
        var sourceRegistry = services.GetRequiredService<IAiSourceRegistry>();
        var logger = services.GetService<ILogger<ZenGardenAiAdapterContributor>>()
            ?? NullLogger<ZenGardenAiAdapterContributor>.Instance;

        // Skip if ZenGarden AI source already registered (explicit config)
        if (sourceRegistry.HasSource(Constants.Discovery.WellKnownServiceName))
        {
            KoanLog.BootDebug(logger, Constants.Logging.Discovery, "skipped",
                ("reason", "source-already-registered"));
            return;
        }

        // Resolve orchestrator endpoint via Zen Garden offering
        var zenGardenProvider = services.GetService<IZenGardenInitializationProvider>();
        if (zenGardenProvider is null)
        {
            KoanLog.BootDebug(logger, Constants.Logging.Discovery, "skipped",
                ("reason", "provider-unavailable"));
            return;
        }

        string? endpoint = null;
        try
        {
            var intent = ZenGardenConnectionIntent.ForOffering(Constants.Discovery.OfferingName);
            var resolved = await zenGardenProvider.Resolve(intent, cancellationToken);
            endpoint = resolved?.GetUri("http", "https");
        }
        catch (Exception ex)
        {
            KoanLog.BootDebug(logger, Constants.Logging.Discovery, "resolution-failed",
                ("error", ex));
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            KoanLog.BootDebug(logger, Constants.Logging.Discovery, "not-found");
            return;
        }

        // Probe the orchestrator
        var http = new HttpClient
        {
            BaseAddress = new Uri(endpoint),
            Timeout = TimeSpan.FromSeconds(10)
        };

        try
        {
            var healthResp = await http.GetAsync(Constants.Endpoints.Health, cancellationToken);
            if (!healthResp.IsSuccessStatusCode)
            {
                KoanLog.BootDebug(logger, Constants.Logging.Discovery, "unhealthy",
                    ("endpoint", endpoint),
                    ("status", (int)healthResp.StatusCode));
                return;
            }
        }
        catch (Exception ex)
        {
            KoanLog.BootDebug(logger, Constants.Logging.Discovery, "unreachable",
                ("endpoint", endpoint),
                ("error", ex));
            return;
        }

        // Discover capabilities
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var capsResp = await http.GetFromJsonAsync<JsonElement>(
                Constants.Endpoints.Capabilities, cancellationToken);

            if (capsResp.ValueKind == JsonValueKind.Array)
            {
                foreach (var cap in capsResp.EnumerateArray())
                {
                    var name = cap.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        capabilities.Add(name);
                }
            }

            KoanLog.BootInfo(logger, Constants.Logging.Discovery, "capabilities-discovered",
                ("endpoint", endpoint),
                ("count", capabilities.Count),
                ("capabilities", string.Join(", ", capabilities)));
        }
        catch (Exception ex)
        {
            KoanLog.BootDebug(logger, Constants.Logging.Discovery, "capabilities-fallback",
                ("error", ex));
            // Assume full capability set if query fails (orchestrator may not have /v1/capabilities yet)
            capabilities = [
                AiCapability.Chat, AiCapability.Embed, AiCapability.Ocr, AiCapability.Vision,
                AiCapability.Streaming, AiCapability.Tools, AiCapability.Imagine,
                AiCapability.Transcribe, AiCapability.Speak, AiCapability.Edit,
                AiCapability.Rerank, AiCapability.Render, AiCapability.Translate,
                AiCapability.Moderate, AiCapability.Pull, AiCapability.ModelList,
            ];
        }

        // Register source
        var member = new AiMemberDefinition
        {
            Name = $"{Constants.Discovery.WellKnownServiceName}::orchestrator",
            ConnectionString = endpoint,
            Order = 0,
            Origin = "zen-garden",
            IsAutoDiscovered = true,
        };

        var source = new AiSourceDefinition
        {
            Name = Constants.Discovery.WellKnownServiceName,
            Provider = Constants.Adapter.Type,
            Priority = 50,
            Policy = "Fallback",
            Members = [member],
            Capabilities = capabilities.ToDictionary(
                c => c,
                _ => new AiCapabilityConfig { Model = "" }),
            Origin = "zen-garden",
            IsAutoDiscovered = true,
        };

        sourceRegistry.RegisterSource(source);

        // Register adapter
        http.Timeout = TimeSpan.FromMinutes(10); // Increase for generation workloads
        var adapter = new ZenGardenAiAdapter(http, logger as ILogger<ZenGardenAiAdapter>
            ?? NullLogger<ZenGardenAiAdapter>.Instance, capabilities);
        adapterRegistry.Add(adapter);

        KoanLog.BootInfo(logger, Constants.Logging.Discovery, "registered",
            ("endpoint", endpoint),
            ("count", capabilities.Count));
    }
}
