namespace Sora.Flow.Infrastructure;

public static class Constants
{
    public static class Sets
    {
        public const string Intake = "intake";
        public const string Standardized = "standardized";
        public const string Keyed = "keyed";
    }

    public static class Views
    {
        public const string Canonical = "canonical";
        public const string Lineage = "lineage";
    }

    public static class Rejections
    {
        public const string NoKeys = "NO_KEYS";
        public const string MultiOwnerCollision = "MULTI_OWNER_COLLISION";
        public const string KeyOwnerMismatch = "KEY_OWNER_MISMATCH";
    }

    public static class Routes
    {
        public const string IntakeRecords = "/intake/records";
        public const string Views = "/views"; // /views/{view}/{referenceId}
        public const string Lineage = "/lineage"; // /lineage/{referenceId}
        public const string Policies = "/policies";
        public const string Admin = "/admin"; // /admin/replay, /admin/reproject
        public const string Control = "/control"; // seed/pull-window/suspend/resume/throttle
        public const string Health = "/healthz";
        public const string Ready = "/readyz";
        public const string Metrics = "/metrics";
    }

    public static class Messaging
    {
        // Default delivery is MQ; expose DLQ names for external monitoring
        public const string ExchangeIntake = "flow.intake";
        public const string ExchangeStandardized = "flow.standardized";
        public const string ExchangeKeyed = "flow.keyed";
        public const string ExchangeAssociation = "flow.association";
        public const string ExchangeProjection = "flow.projection";
        public const string QueueControlPrefix = "control.adapter.";

        public const string DlqIntake = "flow.intake.dlq";
        public const string DlqStandardized = "flow.standardized.dlq";
        public const string DlqKeyed = "flow.keyed.dlq";
        public const string DlqAssociation = "flow.association.dlq";
        public const string DlqProjection = "flow.projection.dlq";
    }
}
