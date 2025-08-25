using Sora.Messaging;

namespace Sora.Messaging.RabbitMq;

internal sealed class RabbitMqOptions
{
    public string? ConnectionString { get; set; }
    public string? ConnectionStringName { get; set; }
    public string Exchange { get; set; } = "sora";
    public string ExchangeType { get; set; } = "topic"; // fanout|topic|direct
    public bool PublisherConfirms { get; set; } = true;
    public int? MaxMessageSizeKB { get; set; }
    public int Prefetch { get; set; } = 50;
    public DlqOptions Dlq { get; set; } = new();
    public RetryOptions Retry { get; set; } = new();
    public bool ProvisionOnStart { get; set; } = false;
    public ProvisioningMode? ProvisionMode { get; set; }
    public List<SubscriptionOption> Subscriptions { get; set; } = new();

    // Optional RabbitMQ Management (HTTP API) overrides for topology inspection
    // If not set, inspector will attempt to derive base URL from connection endpoint (http://host:15672)
    public string? ManagementUrl { get; set; }
    public string? ManagementUsername { get; set; }
    public string? ManagementPassword { get; set; }
}