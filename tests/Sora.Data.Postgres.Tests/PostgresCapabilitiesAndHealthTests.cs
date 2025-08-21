using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Sora.Data.Postgres.Tests;

public class PostgresCapabilitiesAndHealthTests : IClassFixture<PostgresAutoFixture>
{
    private readonly PostgresAutoFixture _fx;

    public PostgresCapabilitiesAndHealthTests(PostgresAutoFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task Capabilities_and_health_are_reported()
    {
        if (_fx.SkipTests) return; // environment lacks Docker; treat as skipped
                                   // Greenfield health: validate adapter contributor reports healthy
        var contributors = _fx.ServiceProvider.GetRequiredService<System.Collections.Generic.IEnumerable<IHealthContributor>>();
        var pg = contributors.First(c => c.Name == "data:postgres");
        var report = await pg.CheckAsync(default);
        report.State.Should().Be(HealthState.Healthy);

        var data = _fx.Data;
        var repo = data.GetRepository<TestEntity, string>();

        var qc = (repo as IQueryCapabilities)!;
        qc.Capabilities.Should().HaveFlag(QueryCapabilities.String);
        qc.Capabilities.Should().HaveFlag(QueryCapabilities.Linq);

        var wc = (repo as IWriteCapabilities)!;
        wc.Writes.Should().HaveFlag(WriteCapabilities.AtomicBatch);
        wc.Writes.Should().HaveFlag(WriteCapabilities.BulkDelete);
        // BulkUpsert intentionally not asserted yet
    }

    public sealed record TestEntity(string Id) : Sora.Data.Abstractions.IEntity<string>
    {
        public string? Name { get; init; }
        public int Age { get; init; }
    }
}
