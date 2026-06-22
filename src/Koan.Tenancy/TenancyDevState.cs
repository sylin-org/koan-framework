namespace Koan.Tenancy;

/// <summary>
/// The in-memory dev control-plane state (ARCH-0099 §1) — populated once at boot by the dev auto-seed under Open
/// posture, empty otherwise. A DI singleton: the seeded dev tenant, the loopback Owner membership, and the
/// branded signing key live here until the durable <c>[HostScoped]</c> control-plane entities land (build-order
/// step 2). <see cref="FallbackTenantId"/> is what an unset ambient scope resolves to in dev (so a developer's
/// ops land in the dev tenant with no day-one 403) — <c>null</c> when not seeded, so prod never falls back.
/// </summary>
public sealed class TenancyDevState
{
    public bool IsSeeded { get; private set; }
    public string? DevTenantId { get; private set; }
    public string? DevTenantName { get; private set; }
    public string? OwnerIdentityId { get; private set; }
    public string? OwnerRole { get; private set; }
    public string? SigningKey { get; private set; }

    /// <summary>The tenant an unset ambient scope falls back to in dev; <c>null</c> when not seeded (prod never falls back).</summary>
    public string? FallbackTenantId => IsSeeded ? DevTenantId : null;

    /// <summary>Apply the dev seed once (idempotent — a second call is ignored).</summary>
    public void Apply(TenancyDevSeed seed)
    {
        if (IsSeeded) return;
        DevTenantId = seed.TenantId;
        DevTenantName = seed.TenantName;
        OwnerIdentityId = seed.OwnerIdentityId;
        OwnerRole = seed.OwnerRole;
        SigningKey = seed.SigningKey;
        IsSeeded = true;
    }
}
