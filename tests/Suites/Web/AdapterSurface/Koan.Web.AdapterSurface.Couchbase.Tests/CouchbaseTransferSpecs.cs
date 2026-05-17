using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Couchbase.Tests;

public sealed class CouchbaseTransferSpecs : AdapterTransferSpecsBase<CouchbaseAdapterFactory>
{
    public CouchbaseTransferSpecs(CouchbaseAdapterFactory factory) : base(factory) { }
}
