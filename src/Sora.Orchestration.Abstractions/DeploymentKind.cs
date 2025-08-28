namespace Sora.Orchestration;

/// <summary>
/// Declares how a service is deployed for dev orchestration.
/// </summary>
public enum DeploymentKind
{
    Container = 0,
    External = 1,
    InProcess = 2
}
