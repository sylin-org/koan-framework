namespace Sora.Messaging.Provisioning;

public sealed record ProvisioningDiagnostics(
    string BusCode,
    string Provider,
    ProvisioningMode Mode,
    DesiredTopology Desired,
    CurrentTopology Current,
    TopologyDiff Diff,
    DateTimeOffset Timestamp);