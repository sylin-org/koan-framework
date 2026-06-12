using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Core;
using Koan.Core.Observability.Health;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Health;

public class SqlServerCapabilitiesAndHealthTests : IClassFixture<Support.SqlServerAutoFixture>
{
    private readonly Support.SqlServerAutoFixture _fx;

    public SqlServerCapabilitiesAndHealthTests(Support.SqlServerAutoFixture fx) => _fx = fx;

    [Fact]
    public async Task Capabilities_and_health_are_reported()
    {
        if (_fx.SkipTests)
        {
            return;
        }

        var contributors = _fx.ServiceProvider.GetRequiredService<IEnumerable<IHealthContributor>>();
        var sql = contributors.First(c => c.Name == "data:sqlserver");
        var report = await sql.Check(default);
        report.State.Should().Be(HealthState.Healthy);

        var data = _fx.Data;
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
