using Koan.ZenGarden.Core;

namespace Koan.AI.Connector.Ollama.Initialization;

internal sealed class OllamaZenGardenOfferingBinding : IZenGardenOfferingBinding
{
    public string AdapterId => "ollama";
    public string Offering => "ollama";
}
