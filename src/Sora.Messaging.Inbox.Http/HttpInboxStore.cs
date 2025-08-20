using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Messaging;
using System.Net.Http.Json;

namespace Sora.Messaging.Inbox.Http;

public sealed class HttpInboxStore : IInboxStore
{
    private readonly HttpClient _http;
    public HttpInboxStore(HttpClient http) => _http = http;

    public async Task<bool> IsProcessedAsync(string key, CancellationToken ct)
    {
        try
        {
            var route = Sora.Messaging.Core.Infrastructure.Constants.Configuration.Inbox.Routes.GetStatus.Replace("{key}", Uri.EscapeDataString(key));
            var resp = await _http.GetAsync(route, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<InboxStatus>(cancellationToken: ct).ConfigureAwait(false);
            return string.Equals(doc?.Status, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Inbox.Values.Processed, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task MarkProcessedAsync(string key, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync(Sora.Messaging.Core.Infrastructure.Constants.Configuration.Inbox.Routes.MarkProcessed, new { key }, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    private sealed class InboxStatus
    {
        public string? Status { get; set; }
    }
}

public static class HttpInboxServiceCollectionExtensions
{
    public static IServiceCollection AddHttpInboxFromConfig(this IServiceCollection services)
    {
        services.AddHttpClient<HttpInboxStore>((sp, http) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var endpoint = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Inbox.Endpoint, null);
            if (string.IsNullOrWhiteSpace(endpoint)) return; // nothing to do
            http.BaseAddress = new Uri(endpoint);
            http.Timeout = TimeSpan.FromSeconds(Sora.Messaging.Core.Infrastructure.Constants.Configuration.Inbox.Values.DefaultTimeoutSeconds);
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
            var endpoint = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Inbox.Endpoint, null);
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                // Explicit endpoint takes precedence over any discovered/default inbox store
                services.Replace(ServiceDescriptor.Singleton<IInboxStore>(sp2 => sp2.GetRequiredService<HttpInboxStore>()));
            }
        }
    }
}
