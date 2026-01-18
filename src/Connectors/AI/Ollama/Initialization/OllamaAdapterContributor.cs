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
using Koan.AI.Connector.Ollama.Infrastructure;
using Koan.AI.Connector.Ollama.Options;
using Koan.Core.Adapters;
using Koan.Core.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.AI.Connector.Ollama.Initialization;

internal sealed class OllamaAdapterContributor : IAiAdapterContributor
{
    public async ValueTask ContributeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var sourceRegistry = services.GetRequiredService<IAiSourceRegistry>();
        var adapterRegistry = services.GetRequiredService<IAiAdapterRegistry>();
        var logger = services.GetService<ILogger<OllamaAdapterContributor>>() ?? NullLogger<OllamaAdapterContributor>.Instance;
        var aiOptions = services.GetService<IOptions<AiOptions>>();
        var ollamaOptionsMonitor = services.GetService<IOptionsMonitor<OllamaOptions>>();
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

            var ollamaConfig = configuration.GetSection("Koan:Ai:Ollama");
            var defaultModel = GetDefaultModel(ollamaConfig, logger);

            var explicitUrls = ollamaConfig.GetSection("Urls").Get<string[]>();
            var additionalUrls = ollamaConfig.GetSection("AdditionalUrls").Get<string[]>();

            ValidateConfiguration(explicitUrls, additionalUrls, logger);

            var members = new List<AiMemberDefinition>();

            if (explicitUrls?.Length > 0)
            {
                // Explicit mode: Honor user selection
                KoanLog.BootInfo(logger, LogActions.Discovery, "explicit-mode", ("count", explicitUrls.Length));
                for (var i = 0; i < explicitUrls.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var url = explicitUrls[i];
                    var caps = await GetCapabilitiesAsync(url, defaultModel, cancellationToken).ConfigureAwait(false);
                    members.Add(new AiMemberDefinition
                    {
                        Name = $"ollama::explicit-{i + 1}",
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
                // Auto-discovery mode: Parallel discovery with priority-based short-circuit
                KoanLog.BootInfo(logger, LogActions.Discovery, "discovery-mode");
                var discovered = await DiscoverInstancesParallel(defaultModel, logger, cancellationToken).ConfigureAwait(false);
                members.AddRange(discovered);
                KoanLog.BootInfo(logger, LogActions.Discovery, "discovered", ("count", discovered.Count));

                if (additionalUrls?.Length > 0)
                {
                    KoanLog.BootInfo(logger, LogActions.Discovery, "additional-urls", ("count", additionalUrls.Length));
                    for (var i = 0; i < additionalUrls.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var url = additionalUrls[i];
                        var caps = await GetCapabilitiesAsync(url, defaultModel, cancellationToken).ConfigureAwait(false);
                        members.Add(new AiMemberDefinition
                        {
                            Name = $"ollama::additional-{i + 1}",
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

            var policy = ResolvePolicy(ollamaConfig, configuration);
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

            RegisterSingletonAdapter(services, configuration, adapterRegistry, logger, ollamaOptionsMonitor, readinessOptions);
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
            logger.LogWarning("Ollama: both 'Urls' and 'AdditionalUrls' specified; AdditionalUrls ignored in explicit mode.");
        }

        if (explicitUrls is { Length: 0 })
        {
            throw new InvalidOperationException("Koan:Ai:Ollama:Urls is empty. Remove to enable discovery or specify at least one URL.");
        }
    }

    /// <summary>
    /// Parallel discovery with priority-based short-circuit:
    /// - Probe all candidates simultaneously
    /// - If top priority responds healthy, cut discovery short
    /// - Otherwise, wait for timeout and pick first healthy by priority
    /// </summary>
    private static async Task<List<AiMemberDefinition>> DiscoverInstancesParallel(string? defaultModel, ILogger logger, CancellationToken ct)
    {
        var candidates = new[]
        {
            (Name: "ollama::host", Url: $"http://{Constants.Discovery.HostDocker}:{Constants.Discovery.DefaultPort}", Priority: 0),
            (Name: "ollama::container", Url: $"http://{Constants.Discovery.WellKnownServiceName}:{Constants.Discovery.DefaultPort}", Priority: 1),
            (Name: "ollama::local", Url: $"http://{Constants.Discovery.Localhost}:{Constants.Discovery.DefaultPort}", Priority: 2)
        };

        var results = new List<AiMemberDefinition>();
        var completionSource = new TaskCompletionSource<bool>();

        // Track results as they come in
        var healthResults = new (string Name, string Url, int Priority, Task<bool> Task)[candidates.Length];

        // Start all probes in parallel
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            var probeTask = ProbeCandidate(candidate.Url, ct);
            healthResults[i] = (candidate.Name, candidate.Url, candidate.Priority, probeTask);
        }

        // Wait for top priority (Priority 0) with a short timeout
        var topPriorityTask = healthResults[0].Task;
        var topPriorityTimeout = Task.Delay(750, ct);
        var firstCompleted = await Task.WhenAny(topPriorityTask, topPriorityTimeout).ConfigureAwait(false);

        if (firstCompleted == topPriorityTask && await topPriorityTask.ConfigureAwait(false))
        {
            // Top priority responded healthy - use it and cut discovery short
            KoanLog.BootInfo(logger, LogActions.Discovery, "top-priority-healthy",
                ("name", candidates[0].Name),
                ("url", candidates[0].Url));

            var caps = await GetCapabilitiesAsync(candidates[0].Url, defaultModel, ct).ConfigureAwait(false);
            results.Add(new AiMemberDefinition
            {
                Name = candidates[0].Name,
                ConnectionString = candidates[0].Url,
                Order = 0,
                Capabilities = caps,
                Origin = "discovered",
                IsAutoDiscovered = true
            });

            return results;
        }

        // Top priority didn't respond in time - wait for all probes with timeout
        var allProbesTimeout = Task.Delay(2000, ct);
        var allProbesTask = Task.WhenAll(healthResults.Select(r => r.Task));
        await Task.WhenAny(allProbesTask, allProbesTimeout).ConfigureAwait(false);

        // Collect healthy candidates, ordered by priority
        var healthyCandidates = new List<(string Name, string Url, int Priority)>();
        for (int i = 0; i < healthResults.Length; i++)
        {
            var result = healthResults[i];
            if (result.Task.IsCompletedSuccessfully && await result.Task.ConfigureAwait(false))
            {
                healthyCandidates.Add((result.Name, result.Url, result.Priority));
                KoanLog.BootDebug(logger, LogActions.Discovery, "candidate-success",
                    ("name", result.Name),
                    ("url", result.Url));
            }
            else
            {
                KoanLog.BootDebug(logger, LogActions.Discovery, "candidate-failed",
                    ("name", result.Name),
                    ("url", result.Url));
            }
        }

        // Sort by priority and create members
        var ordered = healthyCandidates.OrderBy(c => c.Priority).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var candidate = ordered[i];
            var caps = await GetCapabilitiesAsync(candidate.Url, defaultModel, ct).ConfigureAwait(false);
            results.Add(new AiMemberDefinition
            {
                Name = candidate.Name,
                ConnectionString = candidate.Url,
                Order = i,
                Capabilities = caps,
                Origin = "discovered",
                IsAutoDiscovered = true
            });
        }

        return results;
    }

    private static async Task<bool> ProbeCandidate(string baseUrl, CancellationToken ct)
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

    private static Task<IReadOnlyDictionary<string, AiCapabilityConfig>> GetCapabilitiesAsync(string baseUrl, string? defaultModel, CancellationToken ct)
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
        IOptionsMonitor<OllamaOptions>? optionsMonitor,
        IOptions<AdaptersReadinessOptions>? readinessOptions)
    {
        try
        {
            var resolvedOptions = optionsMonitor?.CurrentValue;
            var timeoutSeconds = resolvedOptions?.RequestTimeoutSeconds ?? 180;

            var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            var adapterLogger = services.GetService<ILogger<OllamaAdapter>>() ?? NullLogger<OllamaAdapter>.Instance;

            var adapter = new OllamaAdapter(http, adapterLogger, configuration, readinessOptions?.Value, resolvedOptions);
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

    private static string ResolvePolicy(IConfigurationSection ollamaConfig, IConfiguration configuration)
    {
        return ollamaConfig["Policy"] ?? configuration[$"Koan:Ai:Provider:{Constants.Adapter.Type}:Policy"] ?? "Fallback";
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
    public const string Discovery = "ollama.discovery";
}
