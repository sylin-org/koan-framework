namespace Sora.Messaging.RabbitMq;

public sealed class RabbitMqCapabilities : IMessagingCapabilities
{
    public bool DelayedDelivery => false; // can be true if delayed-exchange plugin is present (future probe)
    public bool DeadLettering => true;
    public bool Transactions => false;
    public int MaxMessageSizeKB => 128;
    public string MessageOrdering => "Partition";
    public bool ScheduledEnqueue => false;
    public bool PublisherConfirms => true;
}