using Newtonsoft.Json;
using Sora.Messaging.Infrastructure;

namespace Sora.Messaging.Inbox.Http;

public sealed class HttpInboxStore : IInboxStore
{
    private readonly HttpClient _http;
    public HttpInboxStore(HttpClient http) => _http = http;

    public async Task<bool> IsProcessedAsync(string key, CancellationToken ct)
    {
        try
        {
            var route = Constants.Configuration.Inbox.Routes.GetStatus.Replace("{key}", Uri.EscapeDataString(key));
            var resp = await _http.GetAsync(route, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            resp.EnsureSuccessStatusCode();
            var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = JsonConvert.DeserializeObject<InboxStatus>(txt);
            return string.Equals(doc?.Status, Constants.Configuration.Inbox.Values.Processed, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task MarkProcessedAsync(string key, CancellationToken ct)
    {
    var payload = JsonConvert.SerializeObject(new { key });
    var resp = await _http.PostAsync(Constants.Configuration.Inbox.Routes.MarkProcessed, new StringContent(payload, System.Text.Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    private sealed class InboxStatus
    {
        public string? Status { get; set; }
    }
}