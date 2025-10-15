using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Redis;
using Koan.Data.Connector.Redis.Infrastructure;
using Koan.Service.Inbox.Connector.Redis.Hosting;
using Koan.Service.Inbox.Connector.Redis.Options;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Service.Inbox.Connector.Redis.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => Infrastructure.Constants.Diagnostics.ModuleName;
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<RedisInboxOptions>(Infrastructure.Constants.Configuration.Section)
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.KeyPrefix))
                {
                    options.KeyPrefix = Infrastructure.Constants.Defaults.KeyPrefix;
                }

                if (options.ProcessingTtl <= TimeSpan.Zero)
                {
                    options.ProcessingTtl = Infrastructure.Constants.Defaults.ProcessingTtl;
                }
            });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, Koan.Data.Connector.Redis.Discovery.RedisDiscoveryAdapter>());

        services.TryAddSingleton<IConnectionMultiplexer>(sp => CreateConnectionMultiplexer(sp));

        if (!services.Any(d => d.ImplementationType == typeof(RedisInboxAnnouncementService)))
        {
            services.AddHostedService<RedisInboxAnnouncementService>();
        }

        var assembly = typeof(KoanAutoRegistrar).Assembly;
        var mvcBuilder = services.AddControllers();
        if (!mvcBuilder.PartManager.ApplicationParts.OfType<AssemblyPart>().Any(p => p.Assembly == assembly))
        {
            mvcBuilder.PartManager.ApplicationParts.Add(new AssemblyPart(assembly));
        }
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var options = new RedisInboxOptions();
        cfg.GetSection(Infrastructure.Constants.Configuration.Section).Bind(options);

        var connectionHint = options.ConnectionString ?? "auto";
        var sanitized = string.Equals(connectionHint, "auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : connectionHint;

        module.AddSetting("ConnectionString", sanitized, isSecret: true);
        module.AddSetting("KeyPrefix", options.KeyPrefix);
        module.AddSetting("ProcessingTtlSeconds", options.ProcessingTtl.TotalSeconds.ToString("n0"));

        module.AddSetting("Messaging.Inbox.Selected", "redis");
        module.AddSetting("Messaging.Inbox.Candidates", "redis");
        module.AddSetting("Messaging.Inbox.Rationale", "Redis inbox module referenced");
    }

    private static IConnectionMultiplexer CreateConnectionMultiplexer(IServiceProvider sp)
    {
        var logger = sp.GetService<ILogger<KoanAutoRegistrar>>();
        var options = sp.GetRequiredService<IOptions<RedisInboxOptions>>().Value;
        var connection = ResolveConnectionString(sp, options);

        logger?.LogInformation("Redis inbox connecting to {ConnectionString}", Koan.Core.Redaction.DeIdentify(connection));

        try
        {
            return ConnectionMultiplexer.Connect(connection);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to connect Redis inbox multiplexer");
            throw;
        }
    }

    private static string ResolveConnectionString(IServiceProvider sp, RedisInboxOptions options)
    {
        if (IsExplicit(options.ConnectionString))
        {
            return options.ConnectionString!;
        }

        var dataOptions = sp.GetService<IOptions<RedisOptions>>()?.Value;
        if (IsExplicit(dataOptions?.ConnectionString))
        {
            return dataOptions!.ConnectionString!;
        }

        if (options.EnableDiscovery)
        {
            var coordinator = sp.GetService<IServiceDiscoveryCoordinator>();
            if (coordinator is not null)
            {
                try
                {
                    var context = new DiscoveryContext
                    {
                        OrchestrationMode = KoanEnv.OrchestrationMode,
                        HealthCheckTimeout = TimeSpan.FromSeconds(10)
                    };

                    var result = coordinator.DiscoverServiceAsync(Constants.Discovery.WellKnownServiceName, context).ConfigureAwait(false).GetAwaiter().GetResult();
                    if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl))
                    {
                        return result.ServiceUrl!;
                    }
                }
                catch
                {
                    // fall back to defaults
                }
            }
        }

        return KoanEnv.InContainer ? Constants.Discovery.DefaultCompose : Constants.Discovery.DefaultLocal;
    }

    private static bool IsExplicit(string? value)
        => !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);
}
