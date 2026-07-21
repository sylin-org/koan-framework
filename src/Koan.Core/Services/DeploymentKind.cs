namespace Koan.Core.Services;

/// <summary>
/// Describes how a service is expected to become available to an application.
/// </summary>
public enum DeploymentKind
{
    Container = 0,
    External = 1,
    InProcess = 2
}
