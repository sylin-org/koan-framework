using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Extensions;

public static class TestContextCouchbaseExtensions
{
    public static bool TryGetCouchbaseFixture(this TestContext context, out CouchbaseContainerFixture fixture, string key = "couchbase")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryGetItem(key, out fixture!);
    }

    public static CouchbaseContainerFixture GetCouchbaseFixture(this TestContext context, string key = "couchbase")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetRequiredItem<CouchbaseContainerFixture>(key);
    }
}
