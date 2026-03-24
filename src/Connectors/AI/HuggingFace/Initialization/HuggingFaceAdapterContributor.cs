using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.AI.Connector.HuggingFace.Initialization;

/// <summary>
/// Registers the HuggingFace adapter into the adapter registry at startup.
/// </summary>
internal sealed class HuggingFaceAdapterContributor : IAiAdapterContributor
{
    public ValueTask Contribute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var registry = services.GetRequiredService<IAiAdapterRegistry>();
        var adapter = services.GetRequiredService<HuggingFaceAdapter>();
        registry.Add(adapter);
        return ValueTask.CompletedTask;
    }
}
