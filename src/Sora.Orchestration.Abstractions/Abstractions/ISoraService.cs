using System.Collections.Generic;

namespace Sora.Orchestration;

/// <summary>
/// Minimal runtime-facing shape for a declared service adapter.
/// </summary>
public interface ISoraService
{
    string ShortCode { get; }
    string Name { get; }
    ServiceKind Kind { get; }
    string? QualifiedCode { get; }
    string? Subtype { get; }
    IReadOnlyDictionary<string, object>? Capabilities { get; }
    IReadOnlyList<int>? Ports { get; }
    string? HealthEndpoint { get; }
    IReadOnlyList<string>? Provides { get; }
    IReadOnlyList<string>? Consumes { get; }
    DeploymentKind DeploymentKind { get; }
    string? ContainerImage { get; }
    string? DefaultTag { get; }
}

/// <summary>
/// Marker for service adapter types; [SoraService] should be applied only to these.
/// </summary>
public interface IServiceAdapter : ISoraService { }
