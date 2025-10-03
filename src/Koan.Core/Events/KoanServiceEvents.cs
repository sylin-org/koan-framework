namespace Koan.Core.Events;

/// <summary>
/// Central catalog of service event name constants to eliminate magic strings.
/// Values preserve existing event identifiers for backward compatibility.
/// </summary>
public static class KoanServiceEvents
{
    public static class Health
    {
        public const string ProbeScheduled = "ProbeScheduled";
        public const string ProbeBroadcast = "ProbeBroadcast";
    }
    public static class Messaging
    {
        public const string Ready = "MessagingReady";
        public const string Failed = "MessagingFailed";
    }
    public static class Translation
    {
        public const string Started = "TranslationStarted";
        public const string Completed = "TranslationCompleted";
        public const string Failed = "TranslationFailed"; // shares name but different semantic domain than Messaging.Failed
    }
    public static class Scheduling
    {
        public const string TaskExecuted = "TaskExecuted";
        public const string TaskFailed = "TaskFailed";
        public const string TaskTimeout = "TaskTimeout";
    }
    public static class Outbox
    {
        public const string Processed = "OutboxProcessed";
        public const string Failed = "OutboxFailed";
    }
    public static class Canon
    {
        public const string EntityProcessed = "CanonEntityProcessed";
        public const string EntityParked = "CanonEntityParked";
        public const string EntityFailed = "CanonEntityFailed";
    }
}
