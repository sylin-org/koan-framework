using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sora.Core;

// Core DI bootstrap
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoraCore(this IServiceCollection services)
    {
        // Default logging: simple console with concise output and sane category levels.
        services.AddLogging(logging =>
        {
            logging.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
            logging.AddFilter("Sora", LogLevel.Information);
        });

    // Best-effort early initialization when provider is built later

    services.AddSingleton<IHealthRegistry, HealthRegistry>();
    services.AddSingleton<IHealthAnnouncementsStore, HealthAnnouncements>();
    services.AddSingleton<IHealthAnnouncer>(sp => (HealthAnnouncements)sp.GetRequiredService<IHealthAnnouncementsStore>());
    services.AddSingleton<IHealthService, HealthService>();
        return services;
    }

    public static IServiceCollection AddHealthContributor<T>(this IServiceCollection services) where T : class, IHealthContributor
        => services.AddSingleton<IHealthContributor, T>();
}
