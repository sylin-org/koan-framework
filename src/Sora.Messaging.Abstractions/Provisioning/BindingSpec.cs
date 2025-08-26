namespace Sora.Messaging.Provisioning;

public sealed record BindingSpec(string FromExchange, string To, string ToType, string RoutingKey = "", IReadOnlyDictionary<string, object?>? Arguments = null);