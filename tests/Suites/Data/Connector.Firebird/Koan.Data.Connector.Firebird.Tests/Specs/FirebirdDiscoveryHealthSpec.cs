using Koan.Core.Orchestration;
using Koan.Data.Connector.Firebird.Discovery;
using Koan.Testing.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Data.Connector.Firebird.Tests.Specs;

/// <summary>
/// Discovery health-validates the whole zero-configuration path: the conventional candidate
/// (SYSDBA/masterkey, relative <c>koan.fdb</c>) must come back healthy against a fresh container
/// even though the Koan database does not exist yet — managed lifecycle creates it before the
/// first DDL. A server that answers with isc_io_error is reachable and authenticated, not down;
/// refusing it made a fresh `docker run firebirdsql/firebird` undiscoverable.
/// </summary>
public sealed class FirebirdDiscoveryHealthSpec(FirebirdFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<FirebirdFixture>(fixture, output)
{
    private string Compose(string database)
    {
        var builder = FirebirdConnectionStrings.Normalize(Fixture.ConnectionString);
        builder.Database = database;
        return builder.ConnectionString;
    }

    [Fact]
    public async Task Existing_database_is_healthy()
    {
        RequireBackingStore();
        var adapter = new ProbeAdapter();
        (await adapter.Validate(Compose("koan.fdb"))).Should().BeTrue();
    }

    [Fact]
    public async Task Absent_database_is_healthy_because_lifecycle_creates_it()
    {
        RequireBackingStore();
        var adapter = new ProbeAdapter();
        var absent = $"/var/lib/firebird/data/absent-{Guid.NewGuid():N}.fdb";
        (await adapter.Validate(Compose(absent))).Should().BeTrue();
    }

    private sealed class ProbeAdapter : FirebirdDiscoveryAdapter
    {
        public ProbeAdapter() : base(
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            NullLogger<FirebirdDiscoveryAdapter>.Instance)
        {
        }

        public Task<bool> Validate(string serviceUrl)
            => ValidateServiceHealth(serviceUrl, new DiscoveryContext
            {
                HealthCheckTimeout = TimeSpan.FromSeconds(10)
            }, CancellationToken.None);
    }
}
