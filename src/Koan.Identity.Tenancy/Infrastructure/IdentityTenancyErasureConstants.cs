namespace Koan.Identity.Tenancy.Infrastructure;

internal static class IdentityTenancyErasureConstants
{
    public const string Owner = "Koan.Identity.Tenancy";
    public const int Order = 200;
    public const int PageSize = 256;
    public const string ErasedActor = "erased-subject";
    public const string ErasedSummary = "Identity lifecycle evidence retained without personal data.";

    public static class Counts
    {
        public const string Memberships = "memberships";
        public const string AgentGrants = "tenant-agent-grants";
        public const string DeprovisioningReceipts = "deprovisioning-receipts-sanitized";
        public const string TenantAuditEntries = "tenant-audit-entries-sanitized";
    }
}
