using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Redis.Orchestration;
using StackExchange.Redis;
using Koan.Orchestration.Aspire;
using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Data.Connector.Redis.Discovery;
using Koan.Core.Provenance;
using RedisItems = Koan.Data.Connector.Redis.Infrastructure.RedisProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Connector.Redis.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    public string ModuleName => "Koan.Data.Connector.Redis";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<RedisOptions>();
        services.AddSingleton<IConfigureOptions<RedisOptions>, RedisOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, RedisHealthContributor>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, RedisOrchestrationEvaluator>());

        // Register Redis discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Redis automatically enables Redis discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, Discovery.RedisDiscoveryAdapter>());

        services.AddSingleton<IDataAdapterFactory, RedisAdapterFactory>();

        // Only register connection multiplexer if Redis is available or in Aspire context
        RegisterConnectionMultiplexer(services);
    }

    private void RegisterConnectionMultiplexer(IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var cs = cfg.ConnectionString;
            if (string.IsNullOrWhiteSpace(cs))
            {
                cs = KoanEnv.InContainer ? Infrastructure.Constants.Discovery.DefaultCompose : Infrastructure.Constants.Discovery.DefaultLocal;
            }

            var logger = sp.GetService<ILogger<KoanAutoRegistrar>>();
            logger?.LogDebug("Attempting Redis connection to: {ConnectionString}", RedactCredentials(cs));

            // ARCH-0080 follow-up: parse to ConfigurationOptions and force tolerant defaults
            // when the user hasn't pinned them. The factory must NEVER throw at host-build time —
            // a missing/unreachable Redis is a runtime concern (health contributors, retry logic
            // in adapters), not a startup-fatal one. Pre-fix callers had to embed
            // `abortConnect=false` in every connection string to avoid eager Connect() throws;
            // now the factory defaults that for them.
            //
            // Parse + Connect are wrapped in the same try so a malformed connection string
            // doesn't slip past as a bare ArgumentException/FormatException.
            var abortConnectImplicitlyDefaulted = !cs.Contains("abortConnect", StringComparison.OrdinalIgnoreCase);
            try
            {
                var options = ConfigurationOptions.Parse(cs);
                if (abortConnectImplicitlyDefaulted)
                {
                    options.AbortOnConnectFail = false;
                }

                var mux = ConnectionMultiplexer.Connect(options);

                // DX guard: with implicit AbortOnConnectFail=false, a typo'd connection string
                // boots silently into a permanently disconnected state. Surface that as a
                // Warning so deployments don't go undetected until first request. Users who
                // pinned abortConnect explicitly know what they signed up for.
                //
                // Skipped when ANY endpoint targets port 0 — that's "deliberately unreachable"
                // sentinel test code uses (per the pillar boot smokes) and the noise would
                // train reviewers to ignore real production occurrences.
                if (abortConnectImplicitlyDefaulted && !mux.IsConnected && !IsDeliberatelyUnreachable(options))
                {
                    logger?.LogWarning(
                        new EventId(1, "RedisDisconnectedBoot"),
                        "Redis multiplexer constructed but NOT connected to {ConnectionString}. " +
                        "AbortOnConnectFail defaulted to false (tolerant) — the host will boot, but cache/coherence " +
                        "operations will fail until Redis becomes reachable. Pin abortConnect=true in the connection " +
                        "string to fail fast on misconfig.",
                        RedactCredentials(cs));
                }

                return mux;
            }
            catch (RedisConnectionException ex)
            {
                // With AbortOnConnectFail=false this branch should be unreachable on transport
                // failure; preserved for genuinely malformed config (auth rejection, etc.) and
                // for callers who pinned abortConnect=true.
                logger?.LogError(ex, "Redis connection failed: {Message}", ex.Message);
                throw new InvalidOperationException($"Redis is not available. Connection string: {RedactCredentials(cs)}. " +
                    "Ensure Redis is running or use the Aspire AppHost for managed Redis.", ex);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                // Parse-time failure: the connection string itself is malformed (not just
                // unreachable). Wrap with the same helpful guidance the connect branch uses.
                logger?.LogError(ex, "Redis connection string is malformed: {Message}", ex.Message);
                throw new InvalidOperationException(
                    $"Redis connection string is malformed and could not be parsed: {RedactCredentials(cs)}. " +
                    "Expected StackExchange.Redis configuration syntax (e.g., 'localhost:6379' or " +
                    "'host1:6379,host2:6379,abortConnect=false'). See https://stackexchange.github.io/StackExchange.Redis/Configuration.",
                    ex);
            }
        });
    }

    /// <summary>
    /// Replaces <c>password=...</c> and <c>user=...</c> segments in a Redis connection string
    /// with <c>***</c> so credentials don't leak into logs or thrown exception messages.
    /// </summary>
    /// <remarks>
    /// The StackExchange.Redis connection-string syntax is comma-separated key=value pairs after
    /// the endpoint list. Match is case-insensitive (the parser is). Pure string work — no
    /// allocations beyond the result.
    /// </remarks>
    internal static string RedactCredentials(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return connectionString;
        // Anchored to comma/start so we don't match a substring that happens to contain "password".
        return System.Text.RegularExpressions.Regex.Replace(
            connectionString,
            @"(?<=^|,)\s*(password|user)\s*=\s*[^,]*",
            "$1=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Returns true when ANY parsed endpoint targets port 0 — the sentinel pillar boot smokes
    /// use to indicate "deliberately unreachable Redis." Skipping the disconnect warning for
    /// this case keeps test runs quiet while preserving the warning for real production typos.
    /// </summary>
    private static bool IsDeliberatelyUnreachable(ConfigurationOptions options)
    {
        foreach (var ep in options.EndPoints)
        {
            // EndPoint can be DnsEndPoint or IPEndPoint; both expose Port.
            switch (ep)
            {
                case System.Net.DnsEndPoint dns when dns.Port == 0: return true;
                case System.Net.IPEndPoint ip when ip.Port == 0: return true;
            }
        }
        return false;
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from RedisDiscoveryAdapter
        module.AddNote("Redis discovery handled by autonomous RedisDiscoveryAdapter");
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");

        // Configure default options for reporting (with provenance)
        var defaultOptions = new RedisOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.ConnectionString}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.ConnectionString}",
            "ConnectionStrings:Redis",
            "ConnectionStrings:Default");

        var database = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.Database,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.Database}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.Database}");

        var defaultPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultPageSize,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.DefaultPageSize}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.DefaultPageSize}");

        var ensureCreated = Configuration.ReadFirstWithSource(
            cfg,
            true,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported}");

        var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionSourceKey = connection.ResolvedKey ??
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.ConnectionString}";

        var effectiveConnectionString = connection.Value ?? defaultOptions.ConnectionString;
        if (connectionIsAuto)
        {
            var adapter = new RedisDiscoveryAdapter(cfg, NullLogger<RedisDiscoveryAdapter>.Instance);
            effectiveConnectionString = AdapterBootReporting.ResolveConnectionString(
                cfg,
                adapter,
                null,
                () => BuildRedisFallback(defaultOptions));
        }

        var connectionMode = connectionIsAuto
            ? ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true)
            : ProvenanceModes.FromConfigurationValue(connection);

        module.PublishConfigValue(
            RedisItems.ConnectionString,
            connection,
            displayOverride: effectiveConnectionString,
            modeOverride: connectionMode,
            usedDefaultOverride: connectionIsAuto ? true : connection.UsedDefault,
            sourceKeyOverride: connectionSourceKey);

        module.PublishConfigValue(RedisItems.Database, database);
        module.PublishConfigValue(RedisItems.EnsureCreatedSupported, ensureCreated);
        module.PublishConfigValue(RedisItems.DefaultPageSize, defaultPageSize);
    }

    private static string BuildRedisFallback(RedisOptions defaults)
    {
        if (!string.IsNullOrWhiteSpace(defaults.ConnectionString) &&
            !string.Equals(defaults.ConnectionString, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return defaults.ConnectionString;
        }

        return KoanEnv.InContainer
            ? Infrastructure.Constants.Discovery.DefaultCompose
            : Infrastructure.Constants.Discovery.DefaultLocal;
    }

    // IKoanAspireRegistrar implementation
    public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = new RedisOptions();
        new RedisOptionsConfigurator(configuration).Configure(options);

        // ARCH-0068: Use static ConnectionStringParser for unified parsing
        var components = Koan.Core.Orchestration.ConnectionStringParser.Parse(
            options.ConnectionString ?? "localhost:6379",
            "redis");

        var redis = builder.AddRedis("redis", port: components.Port)
            .WithDataVolume();

        // Set password if one is provided and not empty
        if (!string.IsNullOrEmpty(components.Password))
        {
            redis.WithEnvironment("REDIS_PASSWORD", components.Password);
        }

        // Set default database if not 0
        if (options.Database != 0)
        {
            redis.WithEnvironment("REDIS_DEFAULT_DB", options.Database.ToString());
        }

        // TODO: Configure proper health check for Redis
        // redis.WithHealthCheck("/health");
    }

    public int Priority => 200; // Cache infrastructure registers after databases but before apps

    public bool ShouldRegister(IConfiguration configuration, IHostEnvironment environment)
    {
        // Register in development environments or when explicitly configured
        return environment.IsDevelopment() || HasExplicitConfiguration(configuration);
    }

    private bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check if there's explicit Redis configuration
        var options = new RedisOptions();
        new RedisOptionsConfigurator(configuration).Configure(options);

        return !string.IsNullOrEmpty(options.ConnectionString) ||
               !string.IsNullOrEmpty(configuration["Redis:ConnectionString"]) ||
               !string.IsNullOrEmpty(configuration["ConnectionStrings:Redis"]);
    }

}


