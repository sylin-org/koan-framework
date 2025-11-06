using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.AI.Connector.LMStudio.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Connector.LMStudio.Infrastructure;
using Koan.Core.Adapters;
using Koan.Core.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.AI.Connector.LMStudio.Initialization;

internal sealed class LMStudioDiscoveryService : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;
    private readonly IAiSourceRegistry _sourceRegistry;
    private readonly IAiAdapterRegistry _adapterRegistry;
    private readonly ILogger<LMStudioDiscoveryService> _logger;
    private readonly IOrchestrationAwareServiceDiscovery _serviceDiscovery;

    public LMStudioDiscoveryService(
        IServiceProvider sp,
        IConfiguration cfg,
        IAiSourceRegistry sourceRegistry,
        IAiAdapterRegistry adapterRegistry)
    {
        _sp = sp;
        _cfg = cfg;
        _sourceRegistry = sourceRegistry;
        _adapterRegistry = adapterRegistry;
        _logger = sp.GetService<ILogger<LMStudioDiscoveryService>>() ?? NullLogger<LMStudioDiscoveryService>.Instance;
        _serviceDiscovery = new OrchestrationAwareServiceDiscovery(cfg, null);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            KoanLog.BootInfo(_logger, LogActions.Discovery, "start");

            if (!ShouldPerformDiscovery())
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "skip", ("reason", "auto-discovery-disabled"));
                return;
            }

            if (_sourceRegistry.HasSource(Constants.Discovery.WellKnownServiceName))
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "skip", ("reason", "source-exists"));
                return;
            }

            var lmConfig = _cfg.GetSection("Koan:Ai:LMStudio");
            var defaultModel = GetDefaultModel(lmConfig);

            var explicitUrls = lmConfig.GetSection("Urls").Get<string[]>();
            var additionalUrls = lmConfig.GetSection("AdditionalUrls").Get<string[]>();

            ValidateConfiguration(explicitUrls, additionalUrls);

            var members = new List<AiMemberDefinition>();

            if (explicitUrls?.Length > 0)
            {
                KoanLog.BootInfo(_logger, LogActions.Discovery, "explicit-mode", ("count", explicitUrls.Length));
                for (var i = 0; i < explicitUrls.Length; i++)
                {
                    var url = explicitUrls[i];
                    var caps = await GetCapabilitiesAsync(url, defaultModel, cancellationToken);
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
                KoanLog.BootInfo(_logger, LogActions.Discovery, "discovery-mode");
                var discovered = await DiscoverInstances(defaultModel, cancellationToken);
                members.AddRange(discovered);
                KoanLog.BootInfo(_logger, LogActions.Discovery, "discovered", ("count", discovered.Count));

                if (additionalUrls?.Length > 0)
                {
                    KoanLog.BootInfo(_logger, LogActions.Discovery, "additional-urls", ("count", additionalUrls.Length));
                    for (var i = 0; i < additionalUrls.Length; i++)
                    {
                        var url = additionalUrls[i];
                        var caps = await GetCapabilitiesAsync(url, defaultModel, cancellationToken);
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
                KoanLog.BootWarning(_logger, LogActions.Discovery, "no-members", ("reason", "no-instances"));
                return;
            }

            var policy = ResolvePolicy(lmConfig);
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

            _sourceRegistry.RegisterSource(source);

            KoanLog.BootInfo(_logger, LogActions.Discovery, "source-registered",
                ("source", source.Name),
                ("members", members.Count),
                ("policy", policy));

            foreach (var member in members)
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "member",
                    ("name", member.Name),
                    ("url", member.ConnectionString));
            }

            RegisterSingletonAdapter();
        }
        catch (Exception ex)
        {
            KoanLog.BootWarning(_logger, LogActions.Discovery, "unexpected-error", ("reason", ex.Message));
            KoanLog.BootDebug(_logger, LogActions.Discovery, "error-detail", ("exception", ex.ToString()));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool ShouldPerformDiscovery()
    {
        var envIsDev = Koan.Core.KoanEnv.IsDevelopment;
        var aiOpts = _sp.GetService<IOptions<Koan.AI.Contracts.Options.AiOptions>>()?.Value;
        var autoDiscovery = aiOpts?.AutoDiscoveryEnabled ?? envIsDev;
        var allowNonDev = aiOpts?.AllowDiscoveryInNonDev ?? true;

        if (!autoDiscovery)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "disabled", ("reason", "AutoDiscoveryEnabled=false"));
            return false;
        }

        if (!envIsDev && !allowNonDev)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "disabled", ("reason", "non-dev-blocked"));
            return false;
        }

        return true;
    }

    private string? GetDefaultModel(IConfigurationSection section)
    {
        var configured = section["DefaultModel"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "default-model", ("model", configured));
            return configured;
        }

        var required = section.GetSection("RequiredModels").Get<string[]>();
        return required?.FirstOrDefault();
    }

    private void ValidateConfiguration(string[]? explicitUrls, string[]? additionalUrls)
    {
        if (explicitUrls?.Length > 0 && additionalUrls?.Length > 0)
        {
            _logger.LogWarning("LM Studio: both 'Urls' and 'AdditionalUrls' specified; AdditionalUrls ignored in explicit mode.");
        }

        if (explicitUrls?.Length == 0)
        {
            throw new InvalidOperationException("Koan:Ai:LMStudio:Urls is empty. Remove to enable discovery or specify at least one URL.");
        }
    }

    private async Task<List<AiMemberDefinition>> DiscoverInstances(string? defaultModel, CancellationToken ct)
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
            if (await IsHealthy(url, ct))
            {
                var caps = await GetCapabilitiesAsync(url, defaultModel, ct);
                results.Add(new AiMemberDefinition
                {
                    Name = name,
                    ConnectionString = url,
                    Order = order,
                    Capabilities = caps,
                    Origin = "discovered",
                    IsAutoDiscovered = true
                });

                KoanLog.BootDebug(_logger, LogActions.Discovery, "candidate-success", ("name", name), ("url", url));
            }
            else
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "candidate-failed", ("name", name), ("url", url));
            }
        }

        return results;
    }

    private async Task<bool> IsHealthy(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(750) };
            var response = await httpClient.GetAsync(new Uri(new Uri(baseUrl.TrimEnd('/')), Constants.Discovery.ModelsPath), ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyDictionary<string, AiCapabilityConfig>> GetCapabilitiesAsync(string baseUrl, string? defaultModel, CancellationToken ct)
    {
        var map = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase);

        var model = defaultModel ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(model))
        {
            map["Chat"] = new AiCapabilityConfig { Model = model };
            map["Embedding"] = new AiCapabilityConfig { Model = model };
        }

        return await Task.FromResult(map);
    }

    private void RegisterSingletonAdapter()
    {
        try
        {
            var resolvedOptions = _sp.GetService<IOptionsMonitor<LMStudioOptions>>()?.CurrentValue;
            var timeoutSeconds = resolvedOptions?.RequestTimeoutSeconds ?? 120;

            var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            var adapterLogger = _sp.GetService<ILogger<LMStudioAdapter>>() ?? NullLogger<LMStudioAdapter>.Instance;
            var readinessDefaults = _sp.GetService<IOptions<AdaptersReadinessOptions>>()?.Value;

            var adapter = new LMStudioAdapter(http, adapterLogger, _cfg, readinessDefaults, resolvedOptions);
            _adapterRegistry.Add(adapter);

            KoanLog.BootInfo(_logger, LogActions.Discovery, "adapter-registered",
                ("adapter", Constants.Adapter.Type),
                ("pattern", "singleton"));
        }
        catch (Exception ex)
        {
            KoanLog.BootWarning(_logger, LogActions.Discovery, "adapter-registration-failed", ("reason", ex.Message));
        }
    }

    private string ResolvePolicy(IConfigurationSection lmConfig)
    {
        return lmConfig["Policy"] ?? _cfg[$"Koan:Ai:Provider:{Constants.Adapter.Type}:Policy"] ?? "Fallback";
    }

    private IReadOnlyDictionary<string, AiCapabilityConfig> BuildSourceCapabilities(IEnumerable<AiMemberDefinition> members, string? defaultModel)
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

