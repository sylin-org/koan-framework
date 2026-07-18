namespace Koan.Identity;

/// <summary>
/// The Identity module's boot posture (mirrors tenancy). <see cref="Closed"/> is the fail-safe default; offline
/// dev-user seeding happens only under <see cref="Open"/>, which is legal only in Development.
/// </summary>
public enum IdentityPosture
{
    /// <summary>Prod-closed: no dev-user auto-seed; never a seeded backdoor identity.</summary>
    Closed = 0,
    /// <summary>Dev-open: offline dev users are seeded for "sign in as alice@example.com" with no network.</summary>
    Open = 1,
}

/// <summary>Pure posture resolution: an explicit typed override wins; otherwise Development → Open, else Closed.</summary>
internal static class IdentityPostureResolver
{
    public static IdentityPosture Resolve(bool isDevelopment, IdentityPosture? overridePosture = null)
        => overridePosture ?? (isDevelopment ? IdentityPosture.Open : IdentityPosture.Closed);
}
