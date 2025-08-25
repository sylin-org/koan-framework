namespace Sora.Messaging.Provisioning;

public sealed record DlqSpec(string? ExchangeName = null, string? RoutingKey = null);