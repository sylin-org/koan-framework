namespace Sora.Messaging;

public interface IMessageBus
{
    Task SendAsync(object message, CancellationToken ct = default);
    Task SendManyAsync(IEnumerable<object> messages, CancellationToken ct = default);
}

public interface IMessageHandler<T>
{
    Task HandleAsync(MessageEnvelope envelope, T message, CancellationToken ct);
}

public sealed record MessageEnvelope(
    string Id,
    string TypeAlias,
    string? CorrelationId,
    string? CausationId,
    IReadOnlyDictionary<string,string> Headers,
    int Attempt,
    DateTimeOffset Timestamp
);

public interface ITypeAliasRegistry
{
    string GetAlias(Type type);
    Type? Resolve(string alias);
}

public interface IMessagingCapabilities
{
    bool DelayedDelivery { get; }
    bool DeadLettering { get; }
    bool Transactions { get; }
    int MaxMessageSizeKB { get; }
    string MessageOrdering { get; } // None | Partition
    bool ScheduledEnqueue { get; }
    bool PublisherConfirms { get; }
}

// Message metadata attributes
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageAttribute : Attribute
{
    public string? Alias { get; set; }
    public int Version { get; set; } = 1;
    public string? Bus { get; set; }
    public string? Group { get; set; }
}

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class PartitionKeyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class HeaderAttribute : Attribute
{
    public HeaderAttribute(string name) { Name = name; }
    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class SensitiveAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class DelaySecondsAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class IdempotencyKeyAttribute : Attribute { }

// Generic batch message wrapper for grouped handling
public sealed class Batch<T>
{
    public Batch() { Items = Array.Empty<T>(); }
    public Batch(IEnumerable<T> items) { Items = items is IReadOnlyList<T> list ? list : items.ToArray(); }
    public IReadOnlyList<T> Items { get; init; }
}
