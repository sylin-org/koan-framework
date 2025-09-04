namespace Sora.Messaging.RabbitMq;

public sealed class SubscriptionOption
{
    public string Name { get; set; } = "default";
    public string? Queue { get; set; }
    public string[] RoutingKeys { get; set; } = Array.Empty<string>();
    public bool Dlq { get; set; } = true;
    public int Concurrency { get; set; } = 1; // number of consumers
}