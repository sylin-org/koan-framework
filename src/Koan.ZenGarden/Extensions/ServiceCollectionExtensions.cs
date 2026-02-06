using Koan.Core.Modules;
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

        services.TryAddSingleton<IZenGardenClient>(sp =>
        {
            var logger = sp.GetService<ILogger<ZenGardenClient>>() ?? NullLogger<ZenGardenClient>.Instance;
            var options = sp.GetService<IOptions<ZenGardenOptions>>()?.Value ?? new ZenGardenOptions();
            return new ZenGardenClient(logger, options);
        });

        return services;
    }
}
