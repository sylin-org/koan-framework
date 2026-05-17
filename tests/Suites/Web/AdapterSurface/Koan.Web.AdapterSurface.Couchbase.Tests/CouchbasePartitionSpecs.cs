using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Couchbase.Tests;

public sealed class CouchbasePartitionSpecs : AdapterPartitionSpecsBase<CouchbaseAdapterFactory>
{
    public CouchbasePartitionSpecs(CouchbaseAdapterFactory factory) : base(factory) { }
}
