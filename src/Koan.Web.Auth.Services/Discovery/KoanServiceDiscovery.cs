using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Services.Options;

namespace Koan.Web.Auth.Services.Discovery;

public sealed class KoanServiceDiscovery : IServiceDiscovery
{
    private readonly ServiceAuthOptions _options;
    private readonly ILogger<KoanServiceDiscovery> _logger;
    private readonly HttpClient _httpClient;

    public KoanServiceDiscovery(
        IOptions<ServiceAuthOptions> options,
        ILogger<KoanServiceDiscovery> logger,
        HttpClient httpClient)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<ServiceEndpoint> ResolveServiceAsync(string serviceId, CancellationToken ct = default)
    {
        _logger.LogDebug("Resolving service endpoint for {ServiceId}", serviceId);

        // 1. Check manual configuration first
        if (_options.ServiceEndpoints.TryGetValue(serviceId, out var manualUrl))
        {
            _logger.LogDebug("Found manual configuration for {ServiceId}: {Url}", serviceId, manualUrl);
            return new ServiceEndpoint(serviceId, new Uri(manualUrl), Array.Empty<string>());
        }

        // 2. Try environment variables
        var envUrl = Environment.GetEnvironmentVariable($"KOAN_SERVICE_{serviceId.ToUpper()}_URL");
        if (!string.IsNullOrEmpty(envUrl))
        {
            _logger.LogDebug("Found environment variable for {ServiceId}: {Url}", serviceId, envUrl);
            return new ServiceEndpoint(serviceId, new Uri(envUrl), Array.Empty<string>());
        }

        // 3. Container-aware resolution (following Koan patterns)
        var candidates = new[]
        {
            $"http://{serviceId}:8080",                           // Docker Compose service name
            $"http://host.docker.internal:{GetPortForService(serviceId)}", // Docker to host
            $"http://localhost:{GetPortForService(serviceId)}"    // Local development
        };

        foreach (var candidate in candidates)
        {
            if (await IsServiceReachable(candidate, ct))
            {
                _logger.LogDebug("Service {ServiceId} reachable at {Url}", serviceId, candidate);
                return new ServiceEndpoint(serviceId, new Uri(candidate), Array.Empty<string>());
            }
        }

        var errorMessage = $"Unable to resolve endpoint for service: {serviceId}. " +
                          $"Tried candidates: {string.Join(", ", candidates)}";
        _logger.LogError(errorMessage);
        throw new ServiceDiscoveryException(serviceId, errorMessage);
    }

    public async Task<ServiceEndpoint[]> DiscoverServicesAsync(CancellationToken ct = default)
    {
        // For now, return empty array - this would be implemented for service registry scenarios
        _logger.LogDebug("Service discovery not yet implemented");
        return Array.Empty<ServiceEndpoint>();
    }

    public Task RegisterServiceAsync(ServiceRegistration registration, CancellationToken ct = default)
    {
        // For now, this is a no-op - would be implemented for service registry scenarios
        _logger.LogDebug("Service registration not yet implemented for {ServiceId}", registration.ServiceId);
        return Task.CompletedTask;
    }

    private static int GetPortForService(string serviceId)
    {
        // Deterministic port assignment for development
        var hash = serviceId.GetHashCode();
        return 8000 + (Math.Abs(hash) % 1000);
    }

    private async Task<bool> IsServiceReachable(string baseUrl, CancellationToken ct)
    {
        try
        {
            // Try to reach the health endpoint or root
            var healthUrl = $"{baseUrl.TrimEnd('/')}/health";
            using var response = await _httpClient.GetAsync(healthUrl, ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // Try root endpoint as fallback
            try
            {
                using var response = await _httpClient.GetAsync(baseUrl, ct);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        catch (TaskCanceledException)
        {
            // Timeout
            return false;
        }
        catch
        {
            return false;
        }
    }
}