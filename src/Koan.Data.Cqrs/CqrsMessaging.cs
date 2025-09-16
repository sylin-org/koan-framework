namespace Koan.Data.Cqrs;

public sealed class CqrsMessaging
{
    public string? Transport { get; set; } // e.g., "RabbitMq"
    public Dictionary<string, string>? Settings { get; set; } // e.g., { ConnectionStringName = "Rabbit" }
}