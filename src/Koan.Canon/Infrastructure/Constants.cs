namespace Koan.Canon.Infrastructure;

internal static class Constants
{
    internal static class Diagnostics
    {
        public const string CapabilityCode = "canon:canonical-entities";
        public const string CapabilitySubject = "canon:composition";
        public const string CapabilityReason = "compiled-canon-plan";
    }

    internal static class Commit
    {
        public const string Canonical = "canonical";
        public const string Indexes = "indexes";
        public const string Audit = "audit";
    }

    internal static class Context
    {
        public const string ExistingEntity = "canon:existing-entity";
        public const string ExistingMetadata = "canon:existing-metadata";
        public const string ArrivalToken = "canon:arrival-token";
        public const string PendingIndexes = "canon:pending-indexes";
        public const string AuditEntries = "canon:audit-entries";
        public const string StageBehavior = "runtime:stage-behavior";
    }
}
