using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Messaging.Infrastructure;

namespace Sora.Messaging.Inbox.Http;

public static class HttpInboxServiceCollectionExtensions
{
    public static IServiceCollection AddHttpInboxFromConfig(this IServiceCollection services)
    {
        services.AddHttpClient<HttpInboxStore>((sp, http) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var endpoint = Configuration.Read<string?>(cfg, Constants.Configuration.Inbox.Endpoint, null);
            if (string.IsNullOrWhiteSpace(endpoint)) return; // nothing to do
            http.BaseAddress = new Uri(endpoint);
            http.Timeout = TimeSpan.FromSeconds(Constants.Configuration.Inbox.Values.DefaultTimeoutSeconds);
        });

        // Auto-discovery initializer: if Endpoint is present, register HttpInboxStore
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(sp => new HttpInboxInitializer()));
        return services;
    }

    private sealed class HttpInboxInitializer : ISoraInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            // Check config dynamically at startup; if endpoint set, wire IInboxStore
            var sp = services.BuildServiceProvider();
            var cfg = sp.GetService<IConfiguration>();
            var endpoint = Configuration.Read<string?>(cfg, Constants.Configuration.Inbox.Endpoint, null);
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                // Explicit endpoint takes precedence over any discovered/default inbox store
                services.Replace(ServiceDescriptor.Singleton<IInboxStore>(sp2 => sp2.GetRequiredService<HttpInboxStore>()));
            }
        }
    }
}