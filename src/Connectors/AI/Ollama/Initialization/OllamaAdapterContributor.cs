using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Koan.ZenGarden.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.AI.Connector.Ollama.Initialization;

internal sealed class OllamaAdapterContributor : IAiAdapterContributor
{
    public async ValueTask Contribute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var sourceRegistry = services.GetRequiredService<IAiSourceRegistry>();
        var adapterRegistry = services.GetRequiredService<IAiAdapterRegistry>();
        var logger = services.GetService<ILogger<OllamaAdapterContributor>>() ?? NullLogger<OllamaAdapterContributor>.Instance;
        var aiOptions = services.GetService<IOptions<AiOptions>>();
        var ollamaOptionsMonitor = services.GetService<IOptionsMonitor<OllamaOptions>>();
        var readinessOptions = services.GetService<IOptions<AdaptersReadinessOptions>>();
        var zenGardenProvider = services.GetService<IZenGardenInitializationProvider>();

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
                RegisterSingletonAdapter(services, configuration, adapterRegistry, logger, ollamaOptionsMonitor, readinessOptions);
                return;
            }

            var ollamaConfig = configuration.GetSection(Constants.Section);
            var defaultModel = GetDefaultModel(ollamaConfig, logger);
            var configuredConnectionString = ollamaConfig["ConnectionString"];
            var explicitUrls = ollamaConfig.GetSection("Urls").Get<string[]>();
            var additionalUrls = ollamaConfig.GetSection("AdditionalUrls").Get<string[]>();
            var requiredCapabilities = ResolveRequiredCapabilities(ollamaConfig);

            ValidateConfiguration(configuredConnectionString, explicitUrls, additionalUrls, logger);

            var members = new List<AiMemberDefinition>();
            var explicitModeRequested = false;

            // Explicit one-off connection string (supports zen-garden://<offering> and direct URLs).
            if (!string.IsNullOrWhiteSpace(configuredConnectionString))
            {
                explicitModeRequested = true;
                if (await TryCreateMemberFromConnection(
                        configuredConnectionString,
                        "ollama::connection",
                        0,
                        "config-connection-string",
                        false,
                        defaultModel,
                        requiredCapabilities,
                        ollamaConfig,
                        zenGardenProvider,
                        logger,
                        cancellationToken).ConfigureAwait(false) is { } configuredMember)
                {
                    members.Add(configuredMember);
                }
                else
                {
                    KoanLog.BootWarning(logger, LogActions.ZenGarden, "connection-string-unresolved", ("value", configuredConnectionString));
                }
            }

            // Explicit URL list mode (each entry can be HTTP URL or zen-garden URI).
            if (explicitUrls?.Length > 0)
            {
                explicitModeRequested = true;
                KoanLog.BootInfo(logger, LogActions.Discovery, "explicit-mode", ("count", explicitUrls.Length));

                for (var i = 0; i < explicitUrls.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var raw = explicitUrls[i];
                    var member = await TryCreateMemberFromConnection(
                        raw,
                        $"ollama::explicit-{i + 1}",
                        members.Count,
                        "config-urls",
                        false,
                        defaultModel,
                        requiredCapabilities,
                        ollamaConfig,
                        zenGardenProvider,
                        logger,
                        cancellationToken).ConfigureAwait(false);
                    if (member is not null)
                    {
                        members.Add(member);
                    }
                }
            }

            // Auto-discovery path: Zen Garden first, connector local discovery fallback.
            if (!explicitModeRequested || members.Count == 0)
            {
                KoanLog.BootInfo(logger, LogActions.Discovery, "discovery-mode");

                var autoDiscovered = false;
                var zenMember = await TryCreateAutoZenGardenMember(
                    ollamaConfig,
                    zenGardenProvider,
                    defaultModel,
                    requiredCapabilities,
                    logger,
                    cancellationToken).ConfigureAwait(false);

                if (zenMember is not null)
                {
                    members.Add(zenMember);
                    autoDiscovered = true;
                    KoanLog.BootInfo(logger, LogActions.ZenGarden, "auto-resolved", ("member", zenMember.ConnectionString));
                }

                if (!autoDiscovered)
                {
                    var discovered = await DiscoverInstancesParallel(defaultModel, logger, cancellationToken).ConfigureAwait(false);
                    members.AddRange(discovered);
                    KoanLog.BootInfo(logger, LogActions.Discovery, "discovered", ("count", discovered.Count));
                }

                if (additionalUrls?.Length > 0)
                {
                    KoanLog.BootInfo(logger, LogActions.Discovery, "additional-urls", ("count", additionalUrls.Length));
                    for (var i = 0; i < additionalUrls.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var raw = additionalUrls[i];
                        var member = await TryCreateMemberFromConnection(
                            raw,
                            $"ollama::additional-{i + 1}",
                            members.Count,
                            "config-additional-urls",
                            false,
                            defaultModel,
                            requiredCapabilities,
                            ollamaConfig,
                            zenGardenProvider,
                            logger,
                            cancellationToken).ConfigureAwait(false);
                        if (member is not null)
                        {
                            members.Add(member);
                        }
                    }
                }
            }

            if (members.Count == 0)
            {
                KoanLog.BootWarning(logger, LogActions.Discovery, "no-members", ("reason", "no-instances"));
                RegisterSingletonAdapter(services, configuration, adapterRegistry, logger, ollamaOptionsMonitor, readinessOptions);
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
                Origin = members.Any(m => string.Equals(m.Origin, "config-urls", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(m.Origin, "config-connection-string", StringComparison.OrdinalIgnoreCase))
                    ? "explicit-config"
                    : "auto-discovery",
                IsAutoDiscovered = members.All(m => m.IsAutoDiscovered)
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
                    ("url", member.ConnectionString),
                    ("origin", member.Origin));
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

    private static async Task<AiMemberDefinition?> TryCreateAutoZenGardenMember(
        IConfigurationSection ollamaConfig,
        IZenGardenInitializationProvider? zenGardenProvider,
        string? defaultModel,
        IReadOnlyList<string> requiredCapabilities,
        ILogger logger,
        CancellationToken ct)
    {
        if (zenGardenProvider is null)
        {
            KoanLog.BootDebug(logger, LogActions.ZenGarden, "provider-missing");
            return null;
        }

        var offering = ResolveZenGardenOffering(ollamaConfig, zenGardenProvider);
        var instance = ResolveZenGardenInstance(ollamaConfig);
        var resolveIntent = ZenGardenConnectionIntent.ForOffering(offering, instance, requiredCapabilities);

        ZenGardenOfferingResolution? resolved;
        try
        {
            resolved = await zenGardenProvider.Resolve(resolveIntent, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            KoanLog.BootWarning(logger, LogActions.ZenGarden, "auto-resolution-failed", ("reason", ex.Message));
            return null;
        }

        if (resolved is null)
        {
            KoanLog.BootDebug(logger, LogActions.ZenGarden, "auto-not-ready", ("offering", resolveIntent.ToOfferingSelector()));
            return null;
        }

        var endpoint = resolved.GetUri("http", "https");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            KoanLog.BootWarning(logger, LogActions.ZenGarden, "auto-missing-endpoint", ("offering", resolved.ToolFqid));
            return null;
        }

        if (!await ProbeCandidate(endpoint, ct).ConfigureAwait(false))
        {
            KoanLog.BootWarning(logger, LogActions.ZenGarden, "auto-unhealthy-endpoint", ("endpoint", endpoint));
            return null;
        }

        var modelHint = ResolveModelHint(defaultModel, requiredCapabilities, resolved);
        var caps = await GetCapabilities(endpoint, modelHint, ct).ConfigureAwait(false);
        return new AiMemberDefinition
        {
            Name = "ollama::zengarden",
            ConnectionString = endpoint,
            Order = 0,
            Capabilities = caps,
            Origin = "zen-garden",
            IsAutoDiscovered = true
        };
    }

    private static async Task<AiMemberDefinition?> TryCreateMemberFromConnection(
        string rawConnection,
        string memberName,
        int order,
        string origin,
        bool autoDiscovered,
        string? defaultModel,
        IReadOnlyList<string> fallbackCapabilities,
        IConfigurationSection ollamaConfig,
        IZenGardenInitializationProvider? zenGardenProvider,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawConnection))
        {
            return null;
        }

        var endpoint = rawConnection;
        var modelHint = defaultModel;

        if (ZenGardenConnectionIntent.TryParse(rawConnection, out var intent))
        {
            if (zenGardenProvider is null)
            {
                KoanLog.BootWarning(logger, LogActions.ZenGarden, "provider-missing-for-intent", ("intent", rawConnection));
                return null;
            }

            intent ??= ZenGardenConnectionIntent.ForOffering(
                ResolveZenGardenOffering(ollamaConfig, zenGardenProvider),
                ResolveZenGardenInstance(ollamaConfig),
                fallbackCapabilities);

            ZenGardenOfferingResolution? resolved;
            try
            {
                resolved = await zenGardenProvider.Resolve(intent, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                KoanLog.BootWarning(logger, LogActions.ZenGarden, "intent-resolution-failed", ("intent", rawConnection), ("reason", ex.Message));
                return null;
            }

            if (resolved is null)
            {
                KoanLog.BootWarning(logger, LogActions.ZenGarden, "intent-not-ready", ("intent", rawConnection));
                return null;
            }

            endpoint = resolved.GetUri("http", "https") ?? "";
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                KoanLog.BootWarning(logger, LogActions.ZenGarden, "intent-missing-endpoint", ("intent", rawConnection));
                return null;
            }

            modelHint = ResolveModelHint(defaultModel, intent.Capabilities, resolved);
        }

        var caps = await GetCapabilities(endpoint, modelHint, ct).ConfigureAwait(false);
        return new AiMemberDefinition
        {
            Name = memberName,
            ConnectionString = endpoint,
            Order = order,
            Capabilities = caps,
            Origin = origin,
            IsAutoDiscovered = autoDiscovered
        };
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
        var requiredModel = required?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requiredModel))
        {
            return requiredModel;
        }

        return "llama3.2";
    }

    private static void ValidateConfiguration(string? configuredConnectionString, string[]? explicitUrls, string[]? additionalUrls, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(configuredConnectionString) && explicitUrls?.Length > 0)
        {
            logger.LogWarning("Ollama: both 'ConnectionString' and 'Urls' are configured; both sets will be evaluated in explicit mode.");
        }

        if (explicitUrls?.Length > 0 && additionalUrls?.Length > 0)
        {
            logger.LogWarning("Ollama: both 'Urls' and 'AdditionalUrls' specified; AdditionalUrls are only considered in fallback auto-discovery mode.");
        }

        if (explicitUrls is { Length: 0 })
        {
            throw new InvalidOperationException("Koan:Ai:Ollama:Urls is empty. Remove to enable discovery or specify at least one URL.");
        }
    }

    private static IReadOnlyList<string> ResolveRequiredCapabilities(IConfigurationSection section)
    {
        var values = new List<string>();
        AppendCapabilityValues(section.GetSection("RequiredCapabilities").Get<string[]>(), values);
        AppendCapabilityValues(section.GetSection("RequiredModels").Get<string[]>(), values);
        AppendCapabilityValues(section.GetSection("ZenGarden:Capabilities").Get<string[]>(), values);
        AppendCapabilityValues(section["ZenGarden:Capability"], values);

        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(v => v.Trim().ToLowerInvariant())
            .ToArray();
        return new ReadOnlyCollection<string>(distinct);
    }

    private static void AppendCapabilityValues(IEnumerable<string>? raw, ICollection<string> output)
    {
        if (raw is null)
        {
            return;
        }

        foreach (var value in raw)
        {
            AppendCapabilityValues(value, output);
        }
    }

    private static void AppendCapabilityValues(string? raw, ICollection<string> output)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var token in raw.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                output.Add(token.Trim());
            }
        }
    }

    private static string ResolveZenGardenOffering(
        IConfigurationSection section,
        IZenGardenInitializationProvider? provider)
    {
        var configured = section["ZenGarden:Offering"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().ToLowerInvariant();
        }

        if (provider?.TryGetDefaultOffering("ollama", out var mapped) == true &&
            !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        return Constants.Discovery.WellKnownServiceName;
    }

    private static string? ResolveZenGardenInstance(IConfigurationSection section)
    {
        var configured = section["ZenGarden:Instance"];
        return string.IsNullOrWhiteSpace(configured)
            ? null
            : configured.Trim().ToLowerInvariant();
    }

    private static string? ResolveModelHint(
        string? defaultModel,
        IReadOnlyList<string> requiredCapabilities,
        ZenGardenOfferingResolution resolved)
    {
        if (resolved.Capabilities.TryGetValue("model", out var models) && models.Count > 0)
        {
            foreach (var required in requiredCapabilities)
            {
                var requiredToken = required.Trim().ToLowerInvariant();
                var separator = requiredToken.IndexOf(':');
                var requiredType = separator > 0 && separator < requiredToken.Length - 1
                    ? requiredToken[..separator]
                    : null;
                var requiredName = separator > 0 && separator < requiredToken.Length - 1
                    ? requiredToken[(separator + 1)..]
                    : requiredToken;

                if (requiredType is not null &&
                    !string.Equals(requiredType, "model", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (models.Any(model => string.Equals(model, requiredName, StringComparison.OrdinalIgnoreCase)))
                {
                    return requiredName;
                }
            }

            return models[0];
        }

        if (!string.IsNullOrWhiteSpace(defaultModel))
        {
            return defaultModel;
        }

        var fallback = requiredCapabilities.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallback))
        {
            return defaultModel ?? "llama3.2";
        }

        var token = fallback.Trim().ToLowerInvariant();
        var fallbackSeparator = token.IndexOf(':');
        if (fallbackSeparator > 0 && fallbackSeparator < token.Length - 1)
        {
            var type = token[..fallbackSeparator];
            if (string.Equals(type, "model", StringComparison.OrdinalIgnoreCase))
            {
                return token[(fallbackSeparator + 1)..];
            }
        }

        return token;
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

            var caps = await GetCapabilities(candidates[0].Url, defaultModel, ct).ConfigureAwait(false);
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
            var caps = await GetCapabilities(candidate.Url, defaultModel, ct).ConfigureAwait(false);
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

    private static Task<IReadOnlyDictionary<string, AiCapabilityConfig>> GetCapabilities(string baseUrl, string? defaultModel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var map = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase);
        var model = defaultModel ?? "";
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

            var http = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            var baseAddress = ResolveAdapterBaseAddress(services, configuration);
            if (baseAddress is not null)
            {
                http.BaseAddress = baseAddress;
                KoanLog.BootDebug(logger, LogActions.Discovery, "adapter-base-address", ("url", baseAddress.ToString()));
            }

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

    private static Uri? ResolveAdapterBaseAddress(IServiceProvider services, IConfiguration configuration)
    {
        var sourceRegistry = services.GetService<IAiSourceRegistry>();
        if (sourceRegistry?.TryGetSource(Constants.Discovery.WellKnownServiceName, out var source) == true)
        {
            var candidate = source?.Members
                .OrderBy(member => member.Order)
                .Select(member => member.ConnectionString)
                .FirstOrDefault(connection => !string.IsNullOrWhiteSpace(connection));
            if (!string.IsNullOrWhiteSpace(candidate) &&
                Uri.TryCreate(candidate, UriKind.Absolute, out var resolvedFromSource))
            {
                return resolvedFromSource;
            }
        }

        var fallbackCandidates = new[]
        {
            configuration[Constants.Configuration.Keys.BaseUrl],
            configuration[Constants.Configuration.Keys.Urls0],
            configuration[Constants.Configuration.Keys.ConnectionString]
        };

        foreach (var raw in fallbackCandidates)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (ZenGardenConnectionIntent.TryParse(raw, out _))
            {
                continue;
            }

            if (Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string ResolvePolicy(IConfigurationSection ollamaConfig, IConfiguration configuration)
    {
        return ollamaConfig["Policy"] ?? configuration[$"Koan:Ai:Provider:{Constants.Adapter.Type}:Policy"] ?? "Fallback";
    }

    private static IReadOnlyDictionary<string, AiCapabilityConfig> BuildSourceCapabilities(IEnumerable<AiMemberDefinition> members, string? defaultModel)
    {
        var map = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase);
        var model = defaultModel ?? members.SelectMany(m => m.Capabilities?.Values ?? []).FirstOrDefault()?.Model ?? "";
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
    public const string ZenGarden = "ollama.zengarden";
}
