namespace Koan.Canon.Infrastructure;

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
        public const string Canonical = "Canonical";
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
        public const string Views = "/views"; // /views/{view}/{ReferenceId}
        public const string Lineage = "/lineage"; // /lineage/{ReferenceId}
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
        public const string ExchangeIntake = "canon.intake";
        public const string ExchangeStandardized = "canon.standardized";
        public const string ExchangeKeyed = "canon.keyed";
        public const string ExchangeAssociation = "canon.association";
        public const string ExchangeProjection = "canon.projection";
        public const string QueueControlPrefix = "control.adapter.";

        public const string DlqIntake = "canon.intake.dlq";
        public const string DlqStandardized = "canon.standardized.dlq";
        public const string DlqKeyed = "canon.keyed.dlq";
        public const string DlqAssociation = "canon.association.dlq";
        public const string DlqProjection = "canon.projection.dlq";
    }

    // Common envelope keys expected in stage payloads (case-insensitive lookups recommended at call sites)
    public static class Envelope
    {
        public const string System = "system";
        public const string Adapter = "adapter";
    }

    // Reserved prefixes and keys used inside intake payload dictionaries ("bag")
    public static class Reserved
    {
        // Prefix for external identifiers provided by adapters, e.g., "identifier.external.deviceId"
        public const string IdentifierExternalPrefix = "identifier.external.";
        // Prefix for parent/related references, e.g., "reference.device", "reference.ulid"
        public const string ReferencePrefix = "reference.";
        // Prefix for model payload scoping, e.g., "model.temperature.celsius"
        public const string ModelPrefix = "model.";
    }
}

