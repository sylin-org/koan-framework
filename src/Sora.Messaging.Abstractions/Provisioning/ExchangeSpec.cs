namespace Sora.Messaging.Provisioning;

public sealed record ExchangeSpec(string Name, string Type, bool Durable = true, bool AutoDelete = false, IReadOnlyDictionary<string, object?>? Arguments = null);