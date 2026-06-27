using Koan.Core;

namespace Koan.Identity.Credentials;

/// <summary>
/// SEC-0007 P3-grp4 — reports whether Koan (not an external IdP) owns a person's PRIMARY sign-in credential (a local
/// password today; a passkey in Phase 2). Discovered; each primary-factor package contributes one. The Security
/// Checkup uses it to stay honest: a Koan-side "add two-factor authentication" nudge is only meaningful when Koan
/// owns the primary factor — an external-IdP-only person relies on their IdP's MFA, so nagging them would be a false
/// alarm.
/// </summary>
[KoanDiscoverable]
public interface IPrimaryCredentialProbe
{
    /// <summary>True when the person has a Koan-managed primary sign-in credential.</summary>
    Task<bool> HasPrimaryAsync(string identityId, CancellationToken ct = default);
}
