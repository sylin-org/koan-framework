using Koan.Web.Auth.Server.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Keys;

/// <summary>
/// SEC-0006 D1 — periodically rotates the persisted ES256 signing key with JWKS overlap. No-op in Development
/// (where an ephemeral per-process key is used) and a cheap timer check otherwise: the store only rotates when
/// the active key has actually exceeded <see cref="AuthServerOptions.KeyRotationInterval"/>.
/// </summary>
internal sealed class IssuerKeyRotationService : BackgroundService
{
    private readonly PersistedIssuerKeyStore _store;
    private readonly AuthServerOptions _options;
    private readonly IHostEnvironment _env;
    private readonly TimeProvider _time;
    private readonly ILogger<IssuerKeyRotationService> _logger;

    public IssuerKeyRotationService(
        PersistedIssuerKeyStore store,
        IOptions<AuthServerOptions> options,
        IHostEnvironment env,
        TimeProvider time,
        ILogger<IssuerKeyRotationService> logger)
    {
        _store = store;
        _options = options.Value;
        _env = env;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Development uses an ephemeral key — nothing to rotate or persist.
        if (_env.IsDevelopment()) return;

        var checkEvery = _options.KeyRotationInterval < TimeSpan.FromHours(4)
            ? TimeSpan.FromMinutes(15)
            : TimeSpan.FromHours(1);
        using var timer = new PeriodicTimer(checkEvery, _time);
        try
        {
            do
            {
                try
                {
                    await _store.RotateIfDueAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "ES256 issuer key rotation check failed; will retry on the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // host shutting down
        }
    }
}
