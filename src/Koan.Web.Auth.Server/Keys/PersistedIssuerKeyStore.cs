using System.Security.Cryptography;
using Koan.Data.Core;
using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Server.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Koan.Web.Auth.Server.Keys;

/// <summary>
/// SEC-0006 D1 — the production ES256 key store: keypairs are persisted <b>encrypted-at-rest</b> (an
/// <c>IDataProtector</c>-protected PKCS#8 blob in a <see cref="IssuerSigningKeyRecord"/> row) so issued tokens
/// survive a restart and the JWKS is stable. Auto-rotates with JWKS overlap — on rotation a fresh key signs and
/// the previous key keeps validating (and stays published) until its tokens expire, then is purged. The active
/// signing key is generated on first boot with zero config (Reference = Intent: referencing
/// <c>Koan.Web.Auth.Server</c> outside Development activates this store, replacing the ephemeral default).
/// <para>
/// The ring is loaded once at startup (<see cref="InitializeAsync"/>, called from the module's async Start) and
/// cached, so the hot-path <see cref="GetKeyRing"/> never touches the data layer or blocks.
/// </para>
/// </summary>
public sealed class PersistedIssuerKeyStore : IIssuerKeyStore
{
    private const string ProtectorPurpose = "Koan.Security.Trust.IssuerSigningKey.v1";

    private readonly IDataProtector _protector;
    private readonly AuthServerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PersistedIssuerKeyStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile EcdsaKeyRing? _ring;

    public PersistedIssuerKeyStore(
        IDataProtectionProvider dataProtection,
        IOptions<AuthServerOptions> options,
        TimeProvider time,
        ILogger<PersistedIssuerKeyStore> logger)
    {
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public EcdsaKeyRing GetKeyRing()
    {
        var ring = _ring;
        if (ring is not null) return ring;
        // Start should have initialized us; block once if a caller raced ahead of startup.
        InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        return _ring!;
    }

    /// <summary>Load (or generate-and-persist) the active key plus any still-valid retiring keys, and cache the ring.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ring is not null) return;
            _ring = await LoadOrCreateAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Rotate the active key if it has exceeded the configured interval; otherwise a no-op.</summary>
    public async Task RotateIfDueAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            var records = await PurgeExpiredAsync(await IssuerSigningKeyRecord.All(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            var active = records.FirstOrDefault(r => r.IsActive);
            if (active is not null && now - active.CreatedUtc < _options.KeyRotationInterval)
            {
                // Not due — but a purge may have removed an expired retiring key, so refresh the cached ring.
                _ring = BuildRing(records, now);
                return;
            }

            if (active is not null)
            {
                active.IsActive = false;
                active.RetireAfterUtc = now + _options.KeyOverlap;
                await active.Save(ct).ConfigureAwait(false);
                _logger.LogInformation("Rotated ES256 issuer signing key {Kid} → retiring (published) until {RetireAfter:o}.", active.Id, active.RetireAfterUtc);
            }

            await GenerateAndPersistActiveAsync(now, ct).ConfigureAwait(false);
            var refreshed = await IssuerSigningKeyRecord.All(ct).ConfigureAwait(false);
            _ring = BuildRing(refreshed, now);
        }
        finally { _gate.Release(); }
    }

    private async Task<EcdsaKeyRing> LoadOrCreateAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var records = await PurgeExpiredAsync(await IssuerSigningKeyRecord.All(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        if (!records.Any(r => r.IsActive))
        {
            await GenerateAndPersistActiveAsync(now, ct).ConfigureAwait(false);
            records = (await IssuerSigningKeyRecord.All(ct).ConfigureAwait(false)).ToList();
        }
        return BuildRing(records, now);
    }

    private async Task<List<IssuerSigningKeyRecord>> PurgeExpiredAsync(IReadOnlyList<IssuerSigningKeyRecord> records, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var survivors = new List<IssuerSigningKeyRecord>(records.Count);
        foreach (var r in records)
        {
            if (!r.IsActive && r.RetireAfterUtc is not null && r.RetireAfterUtc <= now)
            {
                await r.Remove(ct).ConfigureAwait(false);
                _logger.LogInformation("Purged expired ES256 issuer signing key {Kid}.", r.Id);
            }
            else
            {
                survivors.Add(r);
            }
        }
        return survivors;
    }

    private async Task GenerateAndPersistActiveAsync(DateTimeOffset now, CancellationToken ct)
    {
        var key = EcdsaKeyRing.Generate().Active;
        var pkcs8 = key.ECDsa.ExportPkcs8PrivateKey();
        try
        {
            var record = new IssuerSigningKeyRecord
            {
                Id = key.KeyId!,
                ProtectedPkcs8 = Convert.ToBase64String(_protector.Protect(pkcs8)),
                CreatedUtc = now,
                IsActive = true,
            };
            await record.Save(ct).ConfigureAwait(false);
            _logger.LogInformation("Generated + persisted ES256 issuer signing key {Kid}.", key.KeyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    private EcdsaKeyRing BuildRing(IReadOnlyList<IssuerSigningKeyRecord> records, DateTimeOffset now)
    {
        var active = records.FirstOrDefault(r => r.IsActive)
            ?? throw new InvalidOperationException("No active ES256 issuer signing key after load.");
        var retiring = records
            .Where(r => !r.IsActive && (r.RetireAfterUtc is null || r.RetireAfterUtc > now))
            .Select(Import)
            .ToList();
        return new EcdsaKeyRing(Import(active), retiring);
    }

    private ECDsaSecurityKey Import(IssuerSigningKeyRecord record)
    {
        var pkcs8 = _protector.Unprotect(Convert.FromBase64String(record.ProtectedPkcs8));
        try
        {
            var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
            return new ECDsaSecurityKey(ecdsa) { KeyId = record.Id };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }
}
