using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Server.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Keys;

/// <summary>
/// SEC-0006 D1 — periodically rotates the persisted ES256 signing key with JWKS overlap. No-op when the keys are
/// ephemeral (Development without a key ring folder, SEC-0022) and a cheap timer check otherwise: the store only
/// rotates when the active key has actually exceeded <see cref="AuthServerOptions.KeyRotationInterval"/>.
/// </summary>
internal sealed class IssuerKeyRotationService : BackgroundService
{
    private readonly IIssuerKeyStore _store;
    private readonly AuthServerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<IssuerKeyRotationService> _logger;

    public IssuerKeyRotationService(
        IIssuerKeyStore store,
        IOptions<AuthServerOptions> options,
        TimeProvider time,
        ILogger<IssuerKeyRotationService> logger)
    {
        _store = store;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // An ephemeral key has nothing to rotate or persist; the module chose the store (SEC-0022), so its kind is the
        // whole decision and no environment is read here.
        if (_store is not PersistedIssuerKeyStore persisted) return;

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
                    await persisted.RotateIfDueAsync(stoppingToken).ConfigureAwait(false);
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
