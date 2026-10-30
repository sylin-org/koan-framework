using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 — periodically GCs expired OAuth protocol artifacts (consent requests, authorization codes, device
/// codes, refresh tokens, remembered consents, and the expired grants this server issued — SEC-0005) so the data store
/// does not grow without bound. The short-lived artifacts are deleted on
/// success in the request path; this sweeps the ones that were abandoned (never redeemed, denied, or expired).
/// Pairs with the IssuerKeyRotationService precedent.
/// </summary>
internal sealed class OAuthArtifactCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time;
    private readonly IMaintenanceScopes _scopes;
    private readonly ILogger<OAuthArtifactCleanupService> _logger;

    public OAuthArtifactCleanupService(TimeProvider time, IMaintenanceScopes scopes, ILogger<OAuthArtifactCleanupService> logger)
    {
        _time = time;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            do
            {
                try { await Sweep(_scopes, _time.GetUtcNow(), stoppingToken); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "OAuth artifact cleanup failed; will retry on the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>One sweep of everything expired at <paramref name="now"/>. The protocol's own rows are the host's; a remembered
    /// consent and a grant belong to a person inside a scope, so they are swept in each. A grant is this server's when it
    /// names a client; removing an expired one revokes nothing, so it forgets no consent.</summary>
    internal static async Task Sweep(IMaintenanceScopes scopes, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var c in await ConsentRequest.Query(x => x.ExpiresUtc <= now, ct)) await c.Remove(ct);
        foreach (var c in await AuthorizationCode.Query(x => x.ExpiresUtc <= now, ct)) await c.Remove(ct);
        foreach (var d in await DeviceCode.Query(x => x.ExpiresUtc <= now, ct)) await d.Remove(ct);
        foreach (var r in await RefreshToken.Query(x => x.ExpiresUtc <= now, ct)) await r.Remove(ct);
        await scopes.ForEach(async token =>
        {
            foreach (var c in await RememberedConsent.Query(x => x.ExpiresUtc <= now, token)) await c.Remove(token);
            foreach (var g in await AgentGrant.Query(x => x.ClientId != null && x.ExpiresAt <= now, token)) await g.Remove(token);
        }, ct);
    }
}
