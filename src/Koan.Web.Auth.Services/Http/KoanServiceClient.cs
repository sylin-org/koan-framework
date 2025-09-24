using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Koan.Web.Auth.Services.Authentication;
using Koan.Web.Auth.Services.Discovery;

namespace Koan.Web.Auth.Services.Http;

public sealed class KoanServiceClient : IKoanServiceClient
{
    private readonly IServiceAuthenticator _authenticator;
    private readonly IServiceDiscovery _discovery;
    private readonly HttpClient _httpClient;
    private readonly ILogger<KoanServiceClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public KoanServiceClient(
        IServiceAuthenticator authenticator,
        IServiceDiscovery discovery,
        HttpClient httpClient,
        ILogger<KoanServiceClient> logger)
    {
        _authenticator = authenticator;
        _discovery = discovery;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string serviceId, string endpoint, CancellationToken ct = default) where T : class
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        var response = await SendAsync(serviceId, request, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GET request to {ServiceId}{Endpoint} returned {StatusCode}",
                serviceId, endpoint, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async Task<T?> PostAsync<T>(string serviceId, string endpoint, object? data = null, CancellationToken ct = default) where T : class
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        if (data != null)
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await SendAsync(serviceId, request, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("POST request to {ServiceId}{Endpoint} returned {StatusCode}",
                serviceId, endpoint, response.StatusCode);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(responseJson, JsonOptions);
    }

    public async Task<HttpResponseMessage> SendAsync(string serviceId, HttpRequestMessage request, CancellationToken ct = default)
    {
        _logger.LogDebug("Sending {Method} request to {ServiceId}{Endpoint}",
            request.Method, serviceId, request.RequestUri?.ToString());

        // Resolve service endpoint
        var endpoint = await _discovery.ResolveServiceAsync(serviceId, ct);

        // Build absolute URL
        var absoluteUri = new Uri(endpoint.BaseUrl, request.RequestUri?.ToString() ?? "/");
        request.RequestUri = absoluteUri;

        // Get authentication token
        var token = await _authenticator.GetServiceTokenAsync(serviceId, scopes: null, ct);

        // Add authorization header
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Add standard headers
        request.Headers.Add("User-Agent", "Koan-Service-Client/1.0");

        // Send request
        var response = await _httpClient.SendAsync(request, ct);

        _logger.LogDebug("Received {StatusCode} response from {ServiceId}{Endpoint}",
            response.StatusCode, serviceId, request.RequestUri?.PathAndQuery);

        return response;
    }
}