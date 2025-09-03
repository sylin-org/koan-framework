namespace Sora.Messaging.Provisioning;

/// <summary>
/// Central naming strategy for primitives and derived topology objects.
/// </summary>
public interface ITopologyNaming
{
    string CommandRouting(string targetService, string alias, int? version, bool includeVersion);
    string AnnouncementRouting(string domain, string alias, int? version, bool includeVersion);
    string FlowEventRouting(string adapter, string alias);
    string QueueFor(string routingKey, string group);
    string DlqFor(string queueName);
}
