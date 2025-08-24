using Microsoft.Extensions.DependencyInjection;
using Sora.Web.Auth.Options;
using Sora.Web.Auth.Providers;
using Sora.Web.Auth.Domain;
using Sora.Web.Auth.Infrastructure;

namespace Sora.Web.Auth.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoraWebAuth(this IServiceCollection services)
    {
        // Bind from configuration by section path at runtime (no IConfiguration required here)
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionPath).ValidateDataAnnotations();

        services.AddHttpClient();

        services.AddScoped<IProviderRegistry, ProviderRegistry>();
    // Default in-memory stores; apps can replace these via DI with Entity<>-backed implementations.
    services.AddSingleton<IUserStore, InMemoryUserStore>();
    services.AddSingleton<IExternalIdentityStore, InMemoryExternalIdentityStore>();
        return services;
    }
}
