using Koan.AI.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.AI.Connector.HuggingFace.Initialization;

/// <summary>
/// Activates the DI-owned Hugging Face adapter for the host's compiled provider plan.
/// </summary>
internal sealed class HuggingFaceAdapterContributor : IAiProviderActivator
{
    public ValueTask<AiProviderActivation?> Activate(IServiceProvider services, CancellationToken cancellationToken)
    {
        var adapter = services.GetRequiredService<HuggingFaceAdapter>();
        return ValueTask.FromResult<AiProviderActivation?>(new AiProviderActivation { Adapter = adapter });
    }
}
