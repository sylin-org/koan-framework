using System.Net.Http.Json;
using System.Text.Json;
using S13.DocMind.Services;

namespace S13.DocMind.Tools;

public sealed class DocMindProcessingClient : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;
    private bool _disposed;

    public DocMindProcessingClient(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL is required", nameof(baseUrl));
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute)
        };
    }

    public async Task<ProcessingConfigResponse> GetConfigAsync(CancellationToken cancellationToken)
        => await SendGetAsync<ProcessingConfigResponse>("api/processing/config", cancellationToken).ConfigureAwait(false);

    public async Task<ProcessingQueueResult> GetQueueAsync(CancellationToken cancellationToken)
        => await SendGetAsync<ProcessingQueueResult>("api/processing/queue", cancellationToken).ConfigureAwait(false);

    public async Task<ProcessingReplayResult> ReplayAsync(ProcessingReplayRequest request, CancellationToken cancellationToken)
        => await SendPostAsync<ProcessingReplayRequest, ProcessingReplayResult>("api/processing/replay", request, cancellationToken).ConfigureAwait(false);

    public async Task<DocumentDiscoveryValidationResult> ValidateDiscoveryAsync(DocumentDiscoveryValidationRequest request, CancellationToken cancellationToken)
        => await SendPostAsync<DocumentDiscoveryValidationRequest, DocumentDiscoveryValidationResult>("api/processing/discovery/validate", request, cancellationToken).ConfigureAwait(false);

    private async Task<TResponse> SendGetAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken).ConfigureAwait(false);
        return payload ?? throw new InvalidOperationException($"Empty response when retrieving {path}");
    }

    private async Task<TResponse> SendPostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(path, request, SerializerOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken).ConfigureAwait(false);
        return payload ?? throw new InvalidOperationException($"Empty response when posting to {path}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
    }
}
