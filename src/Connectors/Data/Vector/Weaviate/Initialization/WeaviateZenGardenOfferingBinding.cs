using Koan.ZenGarden.Core;

namespace Koan.Data.Vector.Connector.Weaviate.Initialization;

internal sealed class WeaviateZenGardenOfferingBinding : IZenGardenOfferingBinding
{
    public string AdapterId => "weaviate";
    public string Offering => "weaviate";
}

