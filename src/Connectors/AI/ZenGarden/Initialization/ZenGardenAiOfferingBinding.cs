using Koan.ZenGarden.Core;

namespace Koan.AI.Connector.ZenGarden.Initialization;

/// <summary>
/// Binds the ZenGarden AI adapter to the AI orchestrator offering in Zen Garden.
/// </summary>
internal sealed class ZenGardenAiOfferingBinding : IZenGardenOfferingBinding
{
    public string AdapterId => Infrastructure.Constants.Adapter.Id;
    public string Offering => Infrastructure.Constants.Discovery.OfferingName;
}
