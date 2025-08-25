namespace Sora.Messaging.Provisioning;

/// <summary>
/// Provisioning modes for plan/diff/apply.
/// </summary>
public enum ProvisioningMode
{
    Off = 0,
    DryRun = 1,
    CreateIfMissing = 2,
    ReconcileAdditive = 3,
    ForceRecreate = 4
}
