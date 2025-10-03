using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Koan.Data.Connector.SqlServer.Tests;

public class SqlServerCapabilitiesAndHealthTests : IClassFixture<SqlServerAutoFixture>
{
    private readonly SqlServerAutoFixture _fx;

    public SqlServerCapabilitiesAndHealthTests(SqlServerAutoFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task Capabilities_and_health_are_reported()
    {
        // Greenfield health: validate adapter contributor reports healthy
        var contributors = _fx.ServiceProvider.GetRequiredService<System.Collections.Generic.IEnumerable<IHealthContributor>>();
        var sql = contributors.First(c => c.Name == "data:sqlserver");
        var report = await sql.CheckAsync(default);
        report.State.Should().Be(Koan.Core.Observability.Health.HealthState.Healthy);

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

