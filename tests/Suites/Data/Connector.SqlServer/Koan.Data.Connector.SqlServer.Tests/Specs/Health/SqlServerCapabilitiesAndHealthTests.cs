using Koan.Data.Abstractions;
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
        var report = await sql.CheckAsync(default);
        report.State.Should().Be(HealthState.Healthy);

        var data = _fx.Data;
        var repo = data.GetRepository<TestEntity, string>();

        var qc = (repo as IQueryCapabilities)!;
        qc.Capabilities.Should().HaveFlag(QueryCapabilities.String);
        qc.Capabilities.Should().HaveFlag(QueryCapabilities.Linq);

        var wc = (repo as IWriteCapabilities)!;
        wc.Writes.Should().HaveFlag(WriteCapabilities.AtomicBatch);
        wc.Writes.Should().HaveFlag(WriteCapabilities.BulkDelete);
        wc.Writes.Should().HaveFlag(WriteCapabilities.BulkUpsert);
    }

    public sealed record TestEntity(string Id) : IEntity<string>
    {
        public string? Name { get; init; }
        public int Age { get; init; }
    }
}
