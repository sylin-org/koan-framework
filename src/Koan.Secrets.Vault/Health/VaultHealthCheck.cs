using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Koan.Secrets.Vault.Internal;

namespace Koan.Secrets.Vault.Health;

public sealed class VaultHealthCheck(IHttpClientFactory httpClientFactory, IOptions<VaultOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        if (!opts.Enabled)
            return HealthCheckResult.Healthy("Vault disabled");
        try
        {
            var client = httpClientFactory.CreateClient(VaultConstants.HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, "v1/sys/health");
            using var resp = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.TooManyRequests || resp.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                // Vault returns 200/429/503 depending on sealed/standby; treat network success as healthy for reachability
                return HealthCheckResult.Healthy();
            }
            return HealthCheckResult.Degraded($"Vault status: {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Vault unreachable", ex);
        }
    }
}
