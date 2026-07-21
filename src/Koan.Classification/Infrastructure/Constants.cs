namespace Koan.Classification.Infrastructure;

internal static class Constants
{
    internal static class Pipeline
    {
        public const string ContributorId = "classification";
        public const int Order = 100;
    }

    internal static class Keys
    {
        public const string HostScope = "host";
        public const string SegmentedScopePrefix = "seg:";
    }

    internal static class Diagnostics
    {
        public const string CapabilityCode = "koan.classification.field-at-rest.active";
        public const string CapabilitySubject = "classification:field-at-rest";
        public const string CapabilityReason = "compiled-field-transform";
    }
}
