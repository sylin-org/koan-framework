namespace Koan.Data.Cqrs.Outbox.Connector.Mongo;

public sealed class MongoOutboxOptions
{
    public string? ConnectionString { get; set; }
    public string? ConnectionStringName { get; set; } = "mongo";
    public string Database { get; set; } = "Koan";
    public string Collection { get; set; } = "Outbox";
    public int MaxAttempts { get; set; } = 10;
    public int LeaseSeconds { get; set; } = 30;
}
