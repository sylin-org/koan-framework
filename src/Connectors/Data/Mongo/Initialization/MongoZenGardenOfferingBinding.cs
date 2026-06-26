using Koan.ZenGarden.Core;

namespace Koan.Data.Connector.Mongo.Initialization;

internal sealed class MongoZenGardenOfferingBinding : IZenGardenOfferingBinding
{
    public string AdapterId => "mongo";
    public string Offering => "mongodb";
}
