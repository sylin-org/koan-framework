namespace Koan.Classification;

/// <summary>
/// Supplies the ambient tenant id used to select a tenant's field-encryption key (ARCH-0098 §3a). Classification
/// and tenancy are independent axes: the default <see cref="NullClassificationTenantAccessor"/> returns
/// <c>null</c> — the host key bucket — so classification works in a single-tenant app. When per-tenant keying is
/// wanted, an app (or a tenancy integration) replaces this with one that returns the current tenant id, and the
/// crypto-shred / erasure certificate then operates per tenant.
/// </summary>
public interface IClassificationTenantAccessor
{
    /// <summary>The current ambient tenant id, or <c>null</c> for the host bucket.</summary>
    string? CurrentTenantId { get; }
}

/// <summary>The default no-tenant accessor — everything encrypts under the host key. Replaced via DI for multi-tenancy.</summary>
public sealed class NullClassificationTenantAccessor : IClassificationTenantAccessor
{
    public string? CurrentTenantId => null;
}
