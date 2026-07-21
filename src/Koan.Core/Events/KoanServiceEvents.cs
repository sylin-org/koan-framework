namespace Koan.Core.Events;

/// <summary>
/// Central catalog of service event name constants to eliminate magic strings.
/// Values preserve existing event identifiers for backward compatibility.
/// Only events actually emitted/subscribed by the framework's internal
/// background services are kept here (G2 trim); unused event catalogs were removed.
/// </summary>
public static class KoanServiceEvents
{
    public static class Health
    {
        // Emitted/subscribed by Koan.Core/Observability/Health/HealthProbeScheduler.cs
        public const string ProbeScheduled = "ProbeScheduled";
        public const string ProbeBroadcast = "ProbeBroadcast";
    }
    public static class Messaging
    {
        // Emitted/subscribed by Koan.Messaging.Core/MessagingLifecycleService.cs
        public const string Ready = "MessagingReady";
        public const string Failed = "MessagingFailed";
    }
}
