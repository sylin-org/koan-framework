namespace Sora.Messaging.Provisioning;

public interface ITopologyPlanner
{
    /// <param name="busCode">Logical bus code (used in naming).</param>
    /// <param name="defaultGroup">Default consumer group name to use when provider options do not specify subscriptions.</param>
    /// <param name="providerOptions">Provider-specific options object.</param>
    /// <param name="caps">Provider capabilities.</param>
    /// <param name="aliases">Type alias registry for name resolution.</param>
    DesiredTopology Plan(string busCode, string? defaultGroup, object providerOptions, IMessagingCapabilities caps, ITypeAliasRegistry? aliases);
}