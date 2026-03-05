using Koan.Core.AI;
using Koan.Core.Modules;
using Koan.ZenGarden.AI;
using Koan.ZenGarden.Core;
using Koan.ZenGarden.Initialization;
using Koan.ZenGarden.Koi;
using Koan.ZenGarden.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.ZenGarden.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanZenGarden(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<ZenGardenOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configuration is not null)
        {
            services.AddKoanOptions<ZenGardenOptions>(configuration, ZenGardenOptions.SectionName, configure);
        }
        else
        {
            services.AddKoanOptions<ZenGardenOptions>(ZenGardenOptions.SectionName);
            if (configure is not null)
            {
                services.PostConfigure(configure);
            }
        }

        services.TryAddSingleton<IKoiHandler>(sp =>
        {
            var options = sp.GetService<IOptions<ZenGardenOptions>>()?.Value ?? new ZenGardenOptions();
            var logger = sp.GetService<ILogger<KoiHandler>>() ?? NullLogger<KoiHandler>.Instance;
            return new KoiHandler(options, logger);
        });

        services.TryAddSingleton<IZenGardenClient>(sp =>
        {
            var logger = sp.GetService<ILogger<ZenGardenClient>>() ?? NullLogger<ZenGardenClient>.Instance;
            var options = sp.GetService<IOptions<ZenGardenOptions>>()?.Value ?? new ZenGardenOptions();

            IStoneRosterStore? rosterStore = null;
            if (options.PersistDiscoveryCache)
            {
                var path = StoneRosterPathResolver.Resolve(options);
                var ttl = TimeSpan.FromHours(Math.Max(1, options.PersistedCacheTtlHours));
                var storeLogger = sp.GetService<ILogger<StoneRosterStore>>()
                    ?? NullLogger<StoneRosterStore>.Instance;
                rosterStore = new StoneRosterStore(path, ttl, storeLogger);
            }

            var koiHandler = options.KoiDiscoveryEnabled
                ? sp.GetService<IKoiHandler>()
                : null;

            return new ZenGardenClient(logger, options, rosterStore, koiHandler);
        });

        services.TryAddSingleton<IZenGardenInitializationProvider, ZenGardenInitializationProvider>();

        // Register the model advisor — bridges orchestrator recommendations into Koan.AI routing.
        // When both Koan.ZenGarden and Koan.AI are referenced, Client.Chat/Embed/Ocr
        // automatically use the best available model with zero configuration.
        services.TryAddSingleton<IAiModelAdvisor>(sp =>
        {
            var client = sp.GetRequiredService<IZenGardenClient>();
            var options = sp.GetService<IOptions<ZenGardenOptions>>()?.Value ?? new ZenGardenOptions();
            var logger = sp.GetService<ILogger<ZenGardenModelAdvisor>>() ?? NullLogger<ZenGardenModelAdvisor>.Instance;
            return new ZenGardenModelAdvisor(client, options, logger);
        });

        return services;
    }
}
