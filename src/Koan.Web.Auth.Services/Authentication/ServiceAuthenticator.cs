using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Services.Options;

namespace Koan.Web.Auth.Services.Authentication;

/// <summary>
/// SEC-0001 Phase 2 (2i): acquires service-to-service tokens by minting a KSVID in-process from the trust
/// <see cref="IIssuer"/>, replacing the previous HTTP client-credentials round-trip to the TestProvider that
/// used a deterministic, publicly-derivable dev secret (<c>SHA256("koan-dev-secret-{clientId}")</c>). No
/// secret to forge, no internal HTTP hop — and it no longer depends on the now-opt-in TestProvider.
/// (The whole outbound is absorbed into Koan.Security.Trust in 2j; cross-process fleet tokens need the
/// shared/fleet issuer from Phase 5 — the ephemeral dev issuer validates in-process only.)
/// </summary>
public sealed class ServiceAuthenticator : IServiceAuthenticator
{
    private readonly ServiceAuthOptions _options;
    private readonly IMemoryCache _tokenCache;
    private readonly IIssuer _issuer;
    private readonly ILogger<ServiceAuthenticator> _logger;
    private readonly IHostEnvironment _hostEnvironment;

    public ServiceAuthenticator(
        IOptions<ServiceAuthOptions> options,
        IMemoryCache tokenCache,
        IIssuer issuer,
        ILogger<ServiceAuthenticator> logger,
        IHostEnvironment hostEnvironment)
    {
        _options = options.Value;
        _tokenCache = tokenCache;
        _issuer = issuer;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    public async Task<string> GetServiceToken(string targetService, string[]? scopes = null, CancellationToken ct = default)
        => (await GetServiceTokenInfo(targetService, scopes, ct)).AccessToken;

    public Task<ServiceTokenInfo> GetServiceTokenInfo(string targetService, string[]? scopes = null, CancellationToken ct = default)
    {
        scopes ??= _options.DefaultScopes;
        var cacheKey = BuildCacheKey(targetService, scopes);

        if (_options.EnableTokenCaching && _tokenCache.TryGetValue(cacheKey, out ServiceTokenInfo? cached) && cached != null)
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow.Add(_options.TokenRefreshBuffer))
            {
                _logger.LogDebug("Using cached service token for {TargetService}", targetService);
                return Task.FromResult(cached);
            }
            _tokenCache.Remove(cacheKey);
        }

        var tokenInfo = MintToken(targetService, scopes);

        if (_options.EnableTokenCaching)
            _tokenCache.Set(cacheKey, tokenInfo, tokenInfo.ExpiresAt.Subtract(_options.TokenRefreshBuffer));

        return Task.FromResult(tokenInfo);
    }

    public Task InvalidateToken(string targetService, CancellationToken ct = default)
    {
        // Cached tokens self-expire; explicit revocation lands with the epoch mechanism in Phase 3.
        _logger.LogDebug("Service token invalidation requested for {TargetService}", targetService);
        return Task.CompletedTask;
    }

    private ServiceTokenInfo MintToken(string targetService, string[] scopes)
    {
        var clientId = GetClientId();
        var lifetime = TimeSpan.FromHours(1);
        var token = _issuer.Issue(new TrustClaims
        {
            Subject = clientId,
            Permissions = scopes,
            Extra = new Dictionary<string, IReadOnlyList<string>>
            {
                ["token_type"] = new[] { "service" },
                ["target"] = new[] { targetService },
            },
        }, lifetime);

        _logger.LogDebug("Minted in-process service KSVID for {ClientId} -> {TargetService}", clientId, targetService);
        return new ServiceTokenInfo(token, DateTimeOffset.UtcNow.Add(lifetime), scopes);
    }

    private string GetClientId()
    {
        if (!string.IsNullOrEmpty(_options.ClientId)) return _options.ClientId;
        if (_hostEnvironment.IsDevelopment())
            return $"{_hostEnvironment.ApplicationName.ToLowerInvariant()}-service";
        throw new InvalidOperationException("ServiceAuth ClientId must be configured outside Development.");
    }

    private static string BuildCacheKey(string targetService, string[] scopes)
        => $"service_token:{targetService}:{string.Join(",", scopes.OrderBy(s => s))}";
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
