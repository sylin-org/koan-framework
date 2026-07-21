namespace Koan.Identity.Tenancy.Deprovisioning;

/// <summary>Stable names for the exact runtime surfaces described by a <see cref="DeprovisioningReceipt"/>.</summary>
public static class DeprovisioningSurfaces
{
    /// <summary>Tenant-partitioned Entity data, closed when no active membership can establish the tenant axis.</summary>
    public const string TenantData = "tenant-data";

    /// <summary>Tenant-partitioned object storage, closed by the same tenant-axis guarantee.</summary>
    public const string TenantStorage = "tenant-storage";

    /// <summary>Tenant-partitioned cache entries, closed by the same tenant-axis guarantee.</summary>
    public const string TenantCache = "tenant-cache";

    /// <summary>Durable Koan cookie sessions revoked and rechecked by Identity's request-path session guard.</summary>
    public const string CookieSessions = "cookie-sessions";
}
