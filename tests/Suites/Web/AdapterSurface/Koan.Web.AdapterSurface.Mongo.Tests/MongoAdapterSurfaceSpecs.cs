using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Mongo.Tests;

public sealed class MongoAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<MongoAdapterFactory>
{
    public MongoAdapterSurfaceSpecs(MongoAdapterFactory factory) : base(factory) { }
}
