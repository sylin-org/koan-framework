using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Mongo.Tests;

public sealed class MongoPartitionSpecs : AdapterPartitionSpecsBase<MongoAdapterFactory>
{
    public MongoPartitionSpecs(MongoAdapterFactory factory) : base(factory) { }
}
