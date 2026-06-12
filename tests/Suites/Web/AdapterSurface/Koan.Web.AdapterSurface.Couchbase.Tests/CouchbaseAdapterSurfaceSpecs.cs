using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Couchbase.Tests;

public sealed class CouchbaseAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<CouchbaseAdapterFactory>
{
    public CouchbaseAdapterSurfaceSpecs(CouchbaseAdapterFactory factory) : base(factory) { }
}
