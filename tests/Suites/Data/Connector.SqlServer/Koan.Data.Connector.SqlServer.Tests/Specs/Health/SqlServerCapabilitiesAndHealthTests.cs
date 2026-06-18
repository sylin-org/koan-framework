using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Core;
using Koan.Core.Observability.Health;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Health;

public sealed class SqlServerCapabilitiesAndHealthTests(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact]
    public async Task Capabilities_and_health_are_reported()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        var contributors = host.Services.GetRequiredService<IEnumerable<IHealthContributor>>();
        var sql = contributors.First(c => c.Name == "data:sqlserver");
        var report = await sql.Check(default);
        report.State.Should().Be(HealthState.Healthy);

        var data = host.Services.GetRequiredService<IDataService>();
        var repo = data.GetRepository<TestEntity, string>();

        // ARCH-0084: negotiate via the unified CapabilitySet.
        var caps = DataCaps.Describe(repo, repo.GetType().Name);
        caps.Has(DataCaps.Query.String).Should().BeTrue();
        caps.Has(DataCaps.Query.Linq).Should().BeTrue();
        caps.Has(DataCaps.Write.AtomicBatch).Should().BeTrue();
        caps.Has(DataCaps.Write.BulkDelete).Should().BeTrue();
        caps.Has(DataCaps.Write.BulkUpsert).Should().BeTrue();
    }

    public sealed record TestEntity(string Id) : IEntity<string>
    {
        public string? Name { get; init; }
        public int Age { get; init; }
    }
}
