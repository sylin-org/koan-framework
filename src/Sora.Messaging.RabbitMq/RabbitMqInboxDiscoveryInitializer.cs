using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Messaging.Inbox.Http;
using Sora.Messaging.Infrastructure;

namespace Sora.Messaging.RabbitMq;

internal sealed class RabbitMqInboxDiscoveryInitializer : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Evaluate policy using a temporary provider (read-only)
        using var sp = services.BuildServiceProvider();
        var policy = sp.GetService(typeof(IInboxDiscoveryPolicy)) as IInboxDiscoveryPolicy;
        var cfg = sp.GetService(typeof(IConfiguration)) as IConfiguration;
        var endpoint = Configuration.Read<string?>(cfg, Constants.Configuration.Inbox.Endpoint, null);
        if (!string.IsNullOrWhiteSpace(endpoint)) return; // explicit wins
        if (policy is null || !policy.ShouldDiscover(sp))
        {
            try { Console.WriteLine($"[sora][inbox-discovery] skipped: {policy?.Reason(sp) ?? "no-policy"}"); } catch { }
            return;
        }

        // Register an initializer to run after container is finalized to perform async discovery
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(new DeferredInboxWireUp()));
    }

    private sealed class DeferredInboxWireUp : ISoraInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            // Build a provider to run discovery
            using var sp = services.BuildServiceProvider();
            var client = sp.GetService(typeof(IInboxDiscoveryClient)) as IInboxDiscoveryClient;
            var cfg = sp.GetService(typeof(IConfiguration)) as IConfiguration;
            var enabled = sp.GetService(typeof(IOptions<DiscoveryOptions>)) as IOptions<DiscoveryOptions>;
            var timeout = TimeSpan.FromSeconds(Math.Max(1, enabled?.Value.TimeoutSeconds ?? 3));
            string? discovered = null;
            try
            {
                var cts = new CancellationTokenSource(timeout);
                discovered = client is null ? null : client.DiscoverAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(discovered))
            {
                try { Console.WriteLine($"[sora][inbox-discovery] discovered endpoint: {discovered}"); } catch { }
                // Wire HTTP inbox client pointing at the discovered endpoint
                services.AddHttpClient<HttpInboxStore>(http =>
                {
                    http.BaseAddress = new Uri(discovered!);
                    http.Timeout = TimeSpan.FromSeconds(5);
                });
                services.Replace(ServiceDescriptor.Singleton<IInboxStore, HttpInboxStore>());
            }
            else
            {
                try { Console.WriteLine("[sora][inbox-discovery] no endpoint discovered"); } catch { }
            }
        }
    }
}