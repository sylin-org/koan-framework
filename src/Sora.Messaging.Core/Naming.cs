namespace Sora.Messaging;

// Conventional names; adapters can override, but this keeps defaults consistent
public static class Naming
{
    public static string Queue(string busCode, string group)
        => $"sora.{busCode}.{group}";

    public static string RetryExchange(string baseExchange)
        => baseExchange + ".retry";

    public static string DlqExchange(string baseExchange)
        => baseExchange + ".dlq";
}
