using System;
using Koan.Data.Connector.CouchDb;
using Koan.Data.Connector.CouchDb.Runtime;
using Koan.Testing.Containers;

namespace Koan.Data.Connector.CouchDb.Tests.Specs;

/// <summary>
/// Discovery health-validates through the SAME endpoint grammar the connection path accepts: it
/// hands the raw connection string to <see cref="CouchDbClient"/>, which must therefore take a
/// `couchdb://` URI — credentials carried through to the ping — exactly as it takes http(s). This
/// is the cell whose absence let discovery refuse a `couchdb://` connection string the application
/// itself was configured with (R13-19's filed cosmetic finding).
/// </summary>
public sealed class CouchDbDiscoveryHealthSpec(CouchDbFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CouchDbFixture>(fixture, output)
{
    [Fact]
    public async Task Couchdb_uri_connection_string_health_validates()
    {
        RequireBackingStore();
        var http = new Uri(Fixture.ConnectionString);
        var uri = $"couchdb://koan:koan@localhost:{http.Port}";

        using var client = new CouchDbClient(uri, userId: null, password: null);
        var healthy = await client.PingAsync(CancellationToken.None);

        healthy.Should().BeTrue();
    }

    [Fact]
    public async Task Couchdb_uri_without_credentials_still_pings_the_up_endpoint()
    {
        RequireBackingStore();
        var http = new Uri(Fixture.ConnectionString);
        var uri = $"couchdb://localhost:{http.Port}";

        using var client = new CouchDbClient(uri, userId: null, password: null);
        var healthy = await client.PingAsync(CancellationToken.None);

        healthy.Should().BeTrue();
    }
}
