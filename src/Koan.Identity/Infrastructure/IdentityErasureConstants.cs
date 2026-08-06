namespace Koan.Identity.Infrastructure;

internal static class IdentityErasureConstants
{
    public const string PolicyVersion = "identity-erasure/v1";
    public const string CoreOwner = "Koan.Identity.Core";
    public const string AuditOwner = "Koan.Identity.Audit";
    public const string CompositionOwner = "Koan.Identity.Composition";
    public const string ErasedSnapshot = "{\"erased\":true}";
    public const string ErasedTarget = "erased";
    public const int PageSize = 256;

    public static class Counts
    {
        public const string Identities = "identities";
        public const string Emails = "emails";
        public const string Sessions = "sessions";
        public const string ExternalLinks = "external-links";
        public const string GlobalRoles = "global-roles";
        public const string ImpersonationGrants = "impersonation-grants";
        public const string AuditEventsSanitized = "audit-events-sanitized";
        public const string ChainedEventsRehashed = "chained-events-rehashed";
    }
}
