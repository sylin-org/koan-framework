namespace Koan.Core.Actions;

/// <summary>
/// Constants for all Koan service actions to avoid magic strings
/// </summary>
public static class KoanServiceActions
{
    public static class Outbox
    {
        public const string ProcessBatch = "process-batch";
    }

    public static class Messaging
    {
        public const string RestartMessaging = "restart-messaging";
    }

    public static class Flow
    {
        public const string ProcessFlowEntity = "process-flow-entity";
        public const string TriggerProcessing = "trigger-processing";
    }

    public static class Scheduling
    {
        public const string TriggerTask = "trigger-task";
        public const string ListTasks = "list-tasks";
    }

    public static class Health
    {
        public const string ForceProbe = "force-probe";
    }
}