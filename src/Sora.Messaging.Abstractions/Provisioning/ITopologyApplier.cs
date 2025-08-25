namespace Sora.Messaging.Provisioning;

public interface ITopologyApplier
{
    void Apply(string busCode, ProvisioningMode mode, TopologyDiff diff, object providerClient);
}