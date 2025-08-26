namespace Sora.Messaging.Provisioning;

public interface ITopologyDiffer
{
    TopologyDiff Diff(DesiredTopology desired, CurrentTopology current);
}