namespace Koan.Data.Cutover.Infrastructure;

internal static class Constants
{
    internal const string ConfigurationSection = "Koan:Data:Cutover";
    internal const int DefaultPageSize = 500;
    internal const int DefaultContainerPageSize = 100;
    internal const int DefaultMaximumContainers = 4096;

    internal static class FactCodes
    {
        internal const string Planned = "koan.data.cutover.planned";
        internal const string Rejected = "koan.data.cutover.rejected";
        internal const string Pending = "koan.data.cutover.pending";
        internal const string Failed = "koan.data.cutover.failed";
        internal const string EntityVerified = "koan.data.cutover.entity-verified";
        internal const string Completed = "koan.data.cutover.completed";
    }

    internal static class FailureCodes
    {
        internal const string WriterOwnership = "koan.data.cutover.writer-ownership";
        internal const string ProviderEnvelope = "koan.data.cutover.provider-envelope";
        internal const string SourceUnavailable = "koan.data.cutover.source-unavailable";
        internal const string TargetUnavailable = "koan.data.cutover.target-unavailable";
        internal const string TargetPolicy = "koan.data.cutover.target-policy";
        internal const string TargetNotEmpty = "koan.data.cutover.target-not-empty";
        internal const string SourceInventory = "koan.data.cutover.source-inventory";
        internal const string ManifestEmpty = "koan.data.cutover.manifest-empty";
        internal const string Mapping = "koan.data.cutover.mapping";
        internal const string Verification = "koan.data.cutover.verification";
    }
}
