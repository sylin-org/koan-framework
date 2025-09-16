using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Auth.Services.Authentication;

public sealed class ServiceAuthenticationHandler : DelegatingHandler
{
    private readonly IServiceAuthenticator _authenticator;
    private readonly ILogger<ServiceAuthenticationHandler> _logger;

    public ServiceAuthenticationHandler(
        IServiceAuthenticator authenticator,
        ILogger<ServiceAuthenticationHandler> logger)
    {
        _authenticator = authenticator;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Only add authentication if the request doesn't already have an Authorization header
        if (request.Headers.Authorization == null)
        {
            // Extract service ID from request (this is a simplified approach)
            var serviceId = ExtractServiceId(request);
            if (!string.IsNullOrEmpty(serviceId))
            {
                try
                {
                    var token = await _authenticator.GetServiceTokenAsync(serviceId, ct: cancellationToken);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    _logger.LogDebug("Added authentication token for service {ServiceId}", serviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to acquire authentication token for service {ServiceId}", serviceId);
                    // Continue without authentication - let the service decide if it's required
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private string? ExtractServiceId(HttpRequestMessage request)
    {
        // Try to extract service ID from custom header
        if (request.Headers.TryGetValues("X-Koan-Target-Service", out var headerValues))
        {
            return headerValues.FirstOrDefault();
        }

        // Fallback: try to infer from host name
        var host = request.RequestUri?.Host;
        if (!string.IsNullOrEmpty(host) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            // If host follows pattern "service-name.domain" or "service-name", extract service name
            var parts = host.Split('.');
            if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
            {
                return parts[0];
            }
        }

        return null;
    }
}