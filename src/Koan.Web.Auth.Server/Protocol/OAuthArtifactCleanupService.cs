using Koan.Web.Auth.Server.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 — periodically GCs expired OAuth protocol artifacts (consent requests, authorization codes, device
/// codes, refresh tokens) so the data store does not grow without bound. The short-lived artifacts are deleted on
/// success in the request path; this sweeps the ones that were abandoned (never redeemed, denied, or expired).
/// Pairs with the IssuerKeyRotationService precedent.
/// </summary>
internal sealed class OAuthArtifactCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time;
    private readonly ILogger<OAuthArtifactCleanupService> _logger;

    public OAuthArtifactCleanupService(TimeProvider time, ILogger<OAuthArtifactCleanupService> logger)
    {
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            do
            {
                try { await SweepAsync(stoppingToken); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "OAuth artifact cleanup failed; will retry on the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        foreach (var c in await ConsentRequest.Query(x => x.ExpiresUtc <= now, ct)) await c.Remove(ct);
        foreach (var c in await AuthorizationCode.Query(x => x.ExpiresUtc <= now, ct)) await c.Remove(ct);
        foreach (var d in await DeviceCode.Query(x => x.ExpiresUtc <= now, ct)) await d.Remove(ct);
        foreach (var r in await RefreshToken.Query(x => x.ExpiresUtc <= now, ct)) await r.Remove(ct);
    }
}
