namespace Sora.Messaging;

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