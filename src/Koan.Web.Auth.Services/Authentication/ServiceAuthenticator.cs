using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Web.Auth.Services.Options;

namespace Koan.Web.Auth.Services.Authentication;

public sealed class ServiceAuthenticator : IServiceAuthenticator
{
    private readonly ServiceAuthOptions _options;
    private readonly IMemoryCache _tokenCache;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServiceAuthenticator> _logger;
    private readonly IHostEnvironment _hostEnvironment;

    public ServiceAuthenticator(
        IOptions<ServiceAuthOptions> options,
        IMemoryCache tokenCache,
        HttpClient httpClient,
        ILogger<ServiceAuthenticator> logger,
        IHostEnvironment hostEnvironment)
    {
        _options = options.Value;
        _tokenCache = tokenCache;
        _httpClient = httpClient;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    public async Task<string> GetServiceTokenAsync(string targetService, string[]? scopes = null, CancellationToken ct = default)
    {
        var tokenInfo = await GetServiceTokenInfoAsync(targetService, scopes, ct);
        return tokenInfo.AccessToken;
    }

    public async Task<ServiceTokenInfo> GetServiceTokenInfoAsync(string targetService, string[]? scopes = null, CancellationToken ct = default)
    {
        scopes ??= _options.DefaultScopes;
        var cacheKey = BuildCacheKey(targetService, scopes);

        // Check cache first
        if (_options.EnableTokenCaching && _tokenCache.TryGetValue(cacheKey, out ServiceTokenInfo? cachedToken) && cachedToken != null)
        {
            // Check if token is still valid with buffer
            if (cachedToken.ExpiresAt > DateTimeOffset.UtcNow.Add(_options.TokenRefreshBuffer))
            {
                _logger.LogDebug("Using cached token for service {TargetService}", targetService);
                return cachedToken;
            }

            // Remove expired token
            _tokenCache.Remove(cacheKey);
        }

        // Acquire new token
        _logger.LogDebug("Acquiring new token for service {TargetService} with scopes {Scopes}",
            targetService, string.Join(",", scopes));

        var tokenInfo = await AcquireTokenAsync(targetService, scopes, ct);

        // Cache the token
        if (_options.EnableTokenCaching)
        {
            var cacheExpiry = tokenInfo.ExpiresAt.Subtract(_options.TokenRefreshBuffer);
            _tokenCache.Set(cacheKey, tokenInfo, cacheExpiry);
        }

        return tokenInfo;
    }

    public async Task InvalidateTokenAsync(string targetService, CancellationToken ct = default)
    {
        // Remove all cached tokens for this service
        var cacheKeysToRemove = new List<string>();

        // Note: IMemoryCache doesn't provide a way to enumerate keys, so we'd need to track them separately
        // For now, this is a simplified implementation
        _logger.LogDebug("Token invalidation requested for service {TargetService}", targetService);
        await Task.CompletedTask;
    }

    private async Task<ServiceTokenInfo> AcquireTokenAsync(string targetService, string[] scopes, CancellationToken ct)
    {
        var clientId = GetClientId();
        var clientSecret = GetClientSecret(clientId);
        var tokenEndpoint = ResolveTokenEndpoint();

        var requestBody = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        };

        if (scopes.Length > 0)
            requestBody["scope"] = string.Join(' ', scopes);

        _logger.LogDebug("Requesting token from {TokenEndpoint} for client {ClientId}", tokenEndpoint, clientId);

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(requestBody)
        };

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            var errorMessage = $"Token acquisition failed for service {targetService}: {response.StatusCode} - {errorContent}";
            _logger.LogError(errorMessage);
            throw new ServiceAuthenticationException(targetService, scopes, errorMessage);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var tokenResponse = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(json);
        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.access_token))
        {
            var errorMessage = $"Invalid token response for service {targetService}";
            _logger.LogError(errorMessage);
            throw new ServiceAuthenticationException(targetService, scopes, errorMessage);
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.expires_in ?? 3600);
        var grantedScopes = !string.IsNullOrEmpty(tokenResponse.scope)
            ? tokenResponse.scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : scopes;

        _logger.LogInformation("Successfully acquired token for service {TargetService}, expires at {ExpiresAt}",
            targetService, expiresAt);

        return new ServiceTokenInfo(tokenResponse.access_token, expiresAt, grantedScopes);
    }

    private string GetClientId()
    {
        if (!string.IsNullOrEmpty(_options.ClientId))
            return _options.ClientId;

        // Auto-generate client ID in development
        if (_hostEnvironment.IsDevelopment())
        {
            var appName = _hostEnvironment.ApplicationName.ToLowerInvariant();
            return $"{appName}-service";
        }

        throw new InvalidOperationException("ClientId must be configured in production environments");
    }

    private string GetClientSecret(string clientId)
    {
        if (!string.IsNullOrEmpty(_options.ClientSecret))
            return _options.ClientSecret;

        // Auto-generate deterministic secret in development
        if (_hostEnvironment.IsDevelopment())
        {
            return GenerateDevClientSecret(clientId);
        }

        // Check environment variable
        var envSecret = Environment.GetEnvironmentVariable($"KOAN_SERVICE_SECRET_{clientId.ToUpper()}");
        if (!string.IsNullOrEmpty(envSecret))
            return envSecret;

        throw new InvalidOperationException($"ClientSecret must be configured for client {clientId} in production environments");
    }

    private string ResolveTokenEndpoint()
    {
        // For now, assume it's relative to the current host
        // In a real implementation, this would resolve the auth provider endpoint
        var baseUrl = "http://localhost:5007"; // Default TestProvider port
        return $"{baseUrl.TrimEnd('/')}{_options.TokenEndpoint}";
    }

    private static string GenerateDevClientSecret(string clientId)
    {
        // Generate a deterministic secret for development
        var input = $"koan-dev-secret-{clientId}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }

    private static string BuildCacheKey(string targetService, string[] scopes)
    {
        var scopeString = string.Join(",", scopes.OrderBy(s => s));
        return $"service_token:{targetService}:{scopeString}";
    }

    private record TokenResponse(string access_token, string token_type, int? expires_in, string? scope);
}

public class ServiceAuthenticationException : Exception
{
    public string ServiceId { get; }
    public string[] RequestedScopes { get; }

    public ServiceAuthenticationException(string serviceId, string[] scopes, string message)
        : base(message)
    {
        ServiceId = serviceId;
        RequestedScopes = scopes;
    }

    public ServiceAuthenticationException(string serviceId, string[] scopes, string message, Exception innerException)
        : base(message, innerException)
    {
        ServiceId = serviceId;
        RequestedScopes = scopes;
    }
}