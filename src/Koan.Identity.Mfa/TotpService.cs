using Microsoft.Extensions.Options;
using Koan.Data.Core;
using Koan.Identity.Credentials;
using OtpNet;

namespace Koan.Identity.Mfa;

/// <summary>The result of beginning a TOTP enrollment — the <c>otpauth://</c> URI to render as a QR, and the secret to show for manual entry.</summary>
public sealed record TotpEnrollment(string OtpAuthUri, string SecretBase32);

/// <summary>
/// SEC-0007 P3-grp4 — TOTP authenticator enrollment + verification (Otp.NET, RFC 6238). The secret is encrypted at
/// rest via <see cref="IMfaSecretProtector"/>. Enrollment is two-step: <see cref="BeginEnrollmentAsync"/> persists an
/// UNCONFIRMED secret + returns the QR URI; <see cref="ConfirmAsync"/> proves a first code and activates it (only then
/// does MFA gate sign-in). <see cref="VerifyAsync"/> is the step-up check. Anti-replay: a code at/below the last
/// accepted time-step is rejected, so a code cannot be reused inside its own validity window.
/// </summary>
public sealed class TotpService
{
    private readonly IMfaSecretProtector _protector;
    private readonly MfaOptions _options;

    public TotpService(IMfaSecretProtector protector, IOptions<MfaOptions> options)
    {
        _protector = protector;
        _options = options.Value;
    }

    /// <summary>Generate a secret, persist it (protected, UNCONFIRMED), and return the otpauth URI + secret for the QR / manual entry.</summary>
    public async Task<TotpEnrollment> BeginEnrollmentAsync(string identityId, string accountName, string issuer, CancellationToken ct = default)
    {
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secretBase32 = Base32Encoding.ToString(secretBytes);

        await new MfaEnrollment
        {
            Id = MfaEnrollment.KeyFor(identityId, MfaType.Totp),
            IdentityId = identityId,
            Type = MfaType.Totp,
            Secret = _protector.Protect(secretBase32),
            Label = accountName,
            ConfirmedAt = null, // unconfirmed until a first code is proven — does not gate sign-in yet
            LastStepUsed = 0,
        }.Save(ct).ConfigureAwait(false);

        var uri = new OtpUri(OtpType.Totp, secretBase32, accountName, issuer).ToString();
        return new TotpEnrollment(uri, secretBase32);
    }

    /// <summary>Confirm enrollment by proving a code — only then does MFA gate sign-in. Returns false on a bad/expired/replayed code.</summary>
    public Task<bool> ConfirmAsync(string identityId, string code, CancellationToken ct = default)
        => VerifyInternalAsync(identityId, code, requireConfirmed: false, confirmOnSuccess: true, ct);

    /// <summary>Verify a code for a CONFIRMED enrollment (the step-up check). Returns false if unconfirmed / bad / replayed.</summary>
    public Task<bool> VerifyAsync(string identityId, string code, CancellationToken ct = default)
        => VerifyInternalAsync(identityId, code, requireConfirmed: true, confirmOnSuccess: false, ct);

    /// <summary>True when the person has a confirmed TOTP enrollment.</summary>
    public async Task<bool> IsConfirmedAsync(string identityId, CancellationToken ct = default)
        => (await MfaEnrollment.Get(MfaEnrollment.KeyFor(identityId, MfaType.Totp), ct).ConfigureAwait(false))?.IsConfirmed == true;

    private async Task<bool> VerifyInternalAsync(string identityId, string code, bool requireConfirmed, bool confirmOnSuccess, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var enrollment = await MfaEnrollment.Get(MfaEnrollment.KeyFor(identityId, MfaType.Totp), ct).ConfigureAwait(false);
        if (enrollment is null) return false;
        if (requireConfirmed && !enrollment.IsConfirmed) return false;

        var now = DateTimeOffset.UtcNow;
        if (enrollment.IsLockedAt(now)) return false; // brute-force lockout — reject without even checking the code

        var secretBase32 = _protector.Unprotect(enrollment.Secret);
        var totp = new Totp(Base32Encoding.ToBytes(secretBase32));
        var valid = totp.VerifyTotp(code.Trim(), out long matchedStep, new VerificationWindow(previous: 1, future: 1))
                    && matchedStep > enrollment.LastStepUsed; // anti-replay: this code (or an earlier one) was already used

        if (!valid)
        {
            await RegisterFailureAsync(enrollment, now, ct).ConfigureAwait(false);
            return false;
        }

        // Success: advance the anti-replay watermark via a compare-and-set (so two concurrent verifies of the same
        // code can't both win), and clear the lockout counter — atomically with confirmation.
        var priorStep = enrollment.LastStepUsed;
        enrollment.LastStepUsed = matchedStep;
        enrollment.FailedAttempts = 0;
        enrollment.LockedUntil = null;
        if (confirmOnSuccess && !enrollment.IsConfirmed) enrollment.ConfirmedAt = now;

        return await AtomicSingleUse.TryAsync<MfaEnrollment, string>(enrollment, e => e.LastStepUsed == priorStep, ct).ConfigureAwait(false);
    }

    private async Task RegisterFailureAsync(MfaEnrollment enrollment, DateTimeOffset now, CancellationToken ct)
    {
        // Best-effort lockout counter (a lost increment only under-counts — the watermark already gives free single-use).
        enrollment.FailedAttempts++;
        if (enrollment.FailedAttempts >= _options.MaxFailedAttempts)
        {
            enrollment.LockedUntil = now.Add(_options.LockoutDuration);
            enrollment.FailedAttempts = 0;
        }
        await enrollment.Save(ct).ConfigureAwait(false);
    }
}
