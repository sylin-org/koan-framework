using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Mongo.Tests;

public sealed class MongoTransferSpecs : AdapterTransferSpecsBase<MongoAdapterFactory>
{
    public MongoTransferSpecs(MongoAdapterFactory factory) : base(factory) { }
}
