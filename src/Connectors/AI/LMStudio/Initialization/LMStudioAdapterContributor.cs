using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Connector.LMStudio.Infrastructure;
using Koan.AI.Connector.LMStudio.Options;
using Koan.Core.Adapters;
using Koan.Core.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.AI.Connector.LMStudio.Initialization;

internal sealed class LMStudioAdapterContributor : IAiAdapterContributor
{
    public async ValueTask Contribute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var sourceRegistry = services.GetRequiredService<IAiSourceRegistry>();
        var adapterRegistry = services.GetRequiredService<IAiAdapterRegistry>();
        var logger = services.GetService<ILogger<LMStudioAdapterContributor>>() ?? NullLogger<LMStudioAdapterContributor>.Instance;
        var aiOptions = services.GetService<IOptions<AiOptions>>();
        var lmOptionsMonitor = services.GetService<IOptionsMonitor<LMStudioOptions>>();
        var readinessOptions = services.GetService<IOptions<AdaptersReadinessOptions>>();

        try
        {
            KoanLog.BootInfo(logger, LogActions.Discovery, "start");

            if (!ShouldPerformDiscovery(aiOptions, logger))
            {
                KoanLog.BootDebug(logger, LogActions.Discovery, "skip", ("reason", "auto-discovery-disabled"));
                return;
            }

            if (sourceRegistry.HasSource(Constants.Discovery.WellKnownServiceName))
            {
                KoanLog.BootDebug(logger, LogActions.Discovery, "skip", ("reason", "source-exists"));
                return;
            }

            var lmConfig = configuration.GetSection("Koan:Ai:LMStudio");
            var defaultModel = GetDefaultModel(lmConfig, logger);

            var explicitUrls = lmConfig.GetSection("Urls").Get<string[]>();
            var additionalUrls = lmConfig.GetSection("AdditionalUrls").Get<string[]>();

            ValidateConfiguration(explicitUrls, additionalUrls, logger);

            var members = new List<AiMemberDefinition>();

            if (explicitUrls?.Length > 0)
            {
                KoanLog.BootInfo(logger, LogActions.Discovery, "explicit-mode", ("count", explicitUrls.Length));
                for (var i = 0; i < explicitUrls.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var url = explicitUrls[i];
                    var caps = await GetCapabilities(url, defaultModel, cancellationToken).ConfigureAwait(false);
                    members.Add(new AiMemberDefinition
                    {
                        Name = $"lmstudio::explicit-{i + 1}",
                        ConnectionString = url,
                        Order = i,
                        Capabilities = caps,
                        Origin = "config-urls",
                        IsAutoDiscovered = false
                    });
                }
            }
            else
            {
                KoanLog.BootInfo(logger, LogActions.Discovery, "discovery-mode");
                var discovered = await DiscoverInstances(defaultModel, logger, cancellationToken).ConfigureAwait(false);
                members.AddRange(discovered);
                KoanLog.BootInfo(logger, LogActions.Discovery, "discovered", ("count", discovered.Count));

                if (additionalUrls?.Length > 0)
                {
                    KoanLog.BootInfo(logger, LogActions.Discovery, "additional-urls", ("count", additionalUrls.Length));
                    for (var i = 0; i < additionalUrls.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var url = additionalUrls[i];
                        var caps = await GetCapabilities(url, defaultModel, cancellationToken).ConfigureAwait(false);
                        members.Add(new AiMemberDefinition
                        {
                            Name = $"lmstudio::additional-{i + 1}",
                            ConnectionString = url,
                            Order = discovered.Count + i,
                            Capabilities = caps,
                            Origin = "config-additional-urls",
                            IsAutoDiscovered = false
                        });
                    }
                }
            }

            if (members.Count == 0)
            {
                KoanLog.BootWarning(logger, LogActions.Discovery, "no-members", ("reason", "no-instances"));
                return;
            }

            var policy = ResolvePolicy(lmConfig, configuration);
            var source = new AiSourceDefinition
            {
                Name = Constants.Discovery.WellKnownServiceName,
                Provider = Constants.Adapter.Type,
                Priority = 50,
                Policy = policy,
                Members = members,
                Capabilities = BuildSourceCapabilities(members, defaultModel),
                Origin = explicitUrls?.Length > 0 ? "explicit-config" : "auto-discovery",
                IsAutoDiscovered = explicitUrls == null || explicitUrls.Length == 0
            };

            sourceRegistry.RegisterSource(source);

            KoanLog.BootInfo(logger, LogActions.Discovery, "source-registered",
                ("source", source.Name),
                ("members", members.Count),
                ("policy", policy));

            foreach (var member in members)
            {
                KoanLog.BootDebug(logger, LogActions.Discovery, "member",
                    ("name", member.Name),
                    ("url", member.ConnectionString));
            }

            RegisterSingletonAdapter(services, configuration, adapterRegistry, logger, lmOptionsMonitor, readinessOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KoanLog.BootWarning(logger, LogActions.Discovery, "cancelled", ("reason", "startup-cancelled"));
            throw;
        }
        catch (Exception ex)
        {
            KoanLog.BootWarning(logger, LogActions.Discovery, "unexpected-error", ("reason", ex.Message));
            KoanLog.BootDebug(logger, LogActions.Discovery, "error-detail", ("exception", ex.ToString()));
        }
    }

    private static bool ShouldPerformDiscovery(IOptions<AiOptions>? aiOptions, ILogger logger)
    {
        var envIsDev = Koan.Core.KoanEnv.IsDevelopment;
        var options = aiOptions?.Value;
        var autoDiscovery = options?.AutoDiscoveryEnabled ?? envIsDev;
        var allowNonDev = options?.AllowDiscoveryInNonDev ?? true;

        if (!autoDiscovery)
        {
            KoanLog.BootDebug(logger, LogActions.Discovery, "disabled", ("reason", "AutoDiscoveryEnabled=false"));
            return false;
        }

        if (!envIsDev && !allowNonDev)
        {
            KoanLog.BootDebug(logger, LogActions.Discovery, "disabled", ("reason", "non-dev-blocked"));
            return false;
        }

        return true;
    }

    private static string? GetDefaultModel(IConfigurationSection section, ILogger logger)
    {
        var configured = section["DefaultModel"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            KoanLog.BootDebug(logger, LogActions.Discovery, "default-model", ("model", configured));
            return configured;
        }

        var required = section.GetSection("RequiredModels").Get<string[]>();
        return required?.FirstOrDefault();
    }

    private static void ValidateConfiguration(string[]? explicitUrls, string[]? additionalUrls, ILogger logger)
    {
        if (explicitUrls?.Length > 0 && additionalUrls?.Length > 0)
        {
            logger.LogWarning("LM Studio: both 'Urls' and 'AdditionalUrls' specified; AdditionalUrls ignored in explicit mode.");
        }

        if (explicitUrls is { Length: 0 })
        {
            throw new InvalidOperationException("Koan:Ai:LMStudio:Urls is empty. Remove to enable discovery or specify at least one URL.");
        }
    }

    private static async Task<List<AiMemberDefinition>> DiscoverInstances(string? defaultModel, ILogger logger, CancellationToken ct)
    {
        var candidates = new[]
        {
            ("lmstudio::host", $"http://{Constants.Discovery.HostDocker}:{Constants.Discovery.DefaultPort}", 0),
            ("lmstudio::container", $"http://{Constants.Discovery.WellKnownServiceName}:{Constants.Discovery.DefaultPort}", 1),
            ("lmstudio::local", $"http://{Constants.Discovery.Localhost}:{Constants.Discovery.DefaultPort}", 2)
        };

        var results = new List<AiMemberDefinition>();

        foreach (var (name, url, order) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsHealthy(url, ct).ConfigureAwait(false))
            {
                var caps = await GetCapabilities(url, defaultModel, ct).ConfigureAwait(false);
                results.Add(new AiMemberDefinition
                {
                    Name = name,
                    ConnectionString = url,
                    Order = order,
                    Capabilities = caps,
                    Origin = "discovered",
                    IsAutoDiscovered = true
                });

                KoanLog.BootDebug(logger, LogActions.Discovery, "candidate-success", ("name", name), ("url", url));
            }
            else
            {
                KoanLog.BootDebug(logger, LogActions.Discovery, "candidate-failed", ("name", name), ("url", url));
            }
        }

        return results;
    }

    private static async Task<bool> IsHealthy(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(750) };
            var requestUri = new Uri(new Uri(baseUrl.TrimEnd('/')), Constants.Discovery.ModelsPath);
            var response = await httpClient.GetAsync(requestUri, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static Task<IReadOnlyDictionary<string, AiCapabilityConfig>> GetCapabilities(string baseUrl, string? defaultModel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var map = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase);
        var model = defaultModel ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(model))
        {
            map["Chat"] = new AiCapabilityConfig { Model = model };
            map["Embedding"] = new AiCapabilityConfig { Model = model };
        }

        return Task.FromResult<IReadOnlyDictionary<string, AiCapabilityConfig>>(map);
    }

    private static void RegisterSingletonAdapter(
        IServiceProvider services,
        IConfiguration configuration,
        IAiAdapterRegistry adapterRegistry,
        ILogger logger,
        IOptionsMonitor<LMStudioOptions>? optionsMonitor,
        IOptions<AdaptersReadinessOptions>? readinessOptions)
    {
        try
        {
            var resolvedOptions = optionsMonitor?.CurrentValue;
            var timeoutSeconds = resolvedOptions?.RequestTimeoutSeconds ?? 120;

            var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            var adapterLogger = services.GetService<ILogger<LMStudioAdapter>>() ?? NullLogger<LMStudioAdapter>.Instance;

            var adapter = new LMStudioAdapter(http, adapterLogger, configuration, readinessOptions?.Value, resolvedOptions);
            adapterRegistry.Add(adapter);

            KoanLog.BootInfo(logger, LogActions.Discovery, "adapter-registered",
                ("adapter", Constants.Adapter.Type),
                ("pattern", "singleton"));
        }
        catch (Exception ex)
        {
            KoanLog.BootWarning(logger, LogActions.Discovery, "adapter-registration-failed", ("reason", ex.Message));
        }
    }

    private static string ResolvePolicy(IConfigurationSection lmConfig, IConfiguration configuration)
    {
        return lmConfig["Policy"] ?? configuration[$"Koan:Ai:Provider:{Constants.Adapter.Type}:Policy"] ?? "Fallback";
    }

    private static IReadOnlyDictionary<string, AiCapabilityConfig> BuildSourceCapabilities(IEnumerable<AiMemberDefinition> members, string? defaultModel)
    {
        var map = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase);
        var model = defaultModel ?? members.SelectMany(m => m.Capabilities?.Values ?? Array.Empty<AiCapabilityConfig>()).FirstOrDefault()?.Model ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(model))
        {
            map["Chat"] = new AiCapabilityConfig { Model = model };
            map["Embedding"] = new AiCapabilityConfig { Model = model };
        }

        return map;
    }
}

internal static class LogActions
{
    public const string Discovery = "lmstudio.discovery";
}
