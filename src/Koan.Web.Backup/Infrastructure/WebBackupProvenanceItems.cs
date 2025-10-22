using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Backup.Infrastructure;

internal static class WebBackupProvenanceItems
{
    private static readonly string[] SharedProgressConsumers =
    {
        "Koan.Web.Backup.Progress"
    };

    internal static readonly ProvenanceItem BackupWebApi = new(
        "Capability:BackupWebAPI",
        "Backup Web API",
        "HTTP surface for initiating and managing backup operations.",
        DefaultConsumers: new[] { "Koan.Web.Backup.BackupController" });

    internal static readonly ProvenanceItem RestoreWebApi = new(
        "Capability:RestoreWebAPI",
        "Restore Web API",
        "HTTP surface for triggering restore workflows.",
        DefaultConsumers: new[] { "Koan.Web.Backup.RestoreController" });

    internal static readonly ProvenanceItem PollingProgressTracking = new(
        "Capability:PollingProgressTracking",
        "Polling Progress Tracking",
        "Supports client-driven polling of long-running backup operations.",
        DefaultConsumers: new[] { "Koan.Web.Backup.Operations" });

    internal static readonly ProvenanceItem OperationManagement = new(
        "Capability:OperationManagement",
        "Operation Management",
        "Exposes endpoints for monitoring and cancelling backup operations.",
        DefaultConsumers: new[] { "Koan.Web.Backup.Operations" });

    internal static readonly ProvenanceItem BackupCatalogApi = new(
        "Capability:BackupCatalogAPI",
        "Backup Catalog API",
        "Lists exportable resources and available backup bundles.",
        DefaultConsumers: new[] { "Koan.Web.Backup.Catalog" });

    internal static readonly ProvenanceItem BackupVerificationApi = new(
        "Capability:BackupVerificationAPI",
        "Backup Verification API",
        "Validates backup artifacts before restore or export.",
        DefaultConsumers: new[] { "Koan.Web.Backup.Verification" });

    internal static readonly ProvenanceItem SystemStatusApi = new(
        "Capability:SystemStatusAPI",
        "System Status API",
        "Reports service status for hosting environments consuming backups.",
        DefaultConsumers: new[] { "Koan.Web.Backup.Status" });

    internal static readonly ProvenanceItem CorsSupport = new(
        "Capability:CORSSupport",
        "CORS Support",
        "Registers CORS middleware for browser-hosted backup clients.",
        DefaultConsumers: new[] { "Koan.Web.Backup.Middleware" });

    internal static readonly ProvenanceItem ApiVersioning = new(
        "Capability:APIVersioning",
        "API Versioning",
        "Enables versioned routes for the backup HTTP surface.",
        DefaultConsumers: new[] { "Koan.Web.Backup.ApiSurface" });

    internal static readonly ProvenanceItem BackgroundCleanup = new(
        "Capability:BackgroundCleanup",
        "Background Cleanup",
        "Runs scheduled clean-up tasks for completed backup operations.",
        DefaultConsumers: new[] { "Koan.Web.Backup.BackgroundServices" });

    internal static readonly ProvenanceItem ProgressTracking = new(
        "ProgressTracking",
        "Progress Tracking Mode",
        "Describes how clients observe long-running backup operations.",
        DefaultConsumers: SharedProgressConsumers);

    internal static readonly ProvenanceItem SignalRSupport = new(
        "SignalRSupport",
        "SignalR Support",
        "Indicates whether real-time push is enabled alongside polling.",
        DefaultConsumers: SharedProgressConsumers);

    internal static readonly ProvenanceItem PollingInterval = new(
        "PollingInterval",
        "Polling Interval",
        "Details how clients should pace polling requests.",
        DefaultConsumers: SharedProgressConsumers);
}
