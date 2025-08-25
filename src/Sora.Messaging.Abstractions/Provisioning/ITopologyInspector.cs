namespace Sora.Messaging.Provisioning;

public interface ITopologyInspector
{
    CurrentTopology Inspect(string busCode, object providerClient);
}