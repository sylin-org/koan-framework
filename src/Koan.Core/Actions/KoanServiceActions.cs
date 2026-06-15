namespace Koan.Core.Actions;

/// <summary>
/// Constants for Koan service actions to avoid magic strings.
/// Only actions actually consumed by the framework's internal background
/// services are kept here (G2 trim); unused action catalogs were removed.
/// </summary>
public static class KoanServiceActions
{
    public static class Messaging
    {
        // Consumed by Koan.Messaging.Core/MessagingLifecycleService.cs
        public const string RestartMessaging = "restart-messaging";
    }

    public static class Health
    {
        // Consumed by Koan.Core/Observability/Health/HealthProbeScheduler.cs
        public const string ForceProbe = "force-probe";
    }
}
