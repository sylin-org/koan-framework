using System.ComponentModel;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Providers;

/// <summary>
/// Framework-facing runtime activation seam for an AI provider declared by a referenced Koan module.
/// Activators resolve a DI-owned adapter and describe the sources that are actually available to this host.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IAiProviderActivator
{
    ValueTask<AiProviderActivation?> Activate(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>A provider adapter and the sources it makes routable in this host.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record AiProviderActivation
{
    public required IAiAdapter Adapter { get; init; }
    public IReadOnlyList<AiSourceDefinition> Sources { get; init; } = [];
}
