using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Xunit;

namespace Sora.Data.SqlServer.Tests;

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
    var hs = _fx.ServiceProvider.GetRequiredService<Sora.Core.IHealthService>();
    var health = await hs.CheckAllAsync(default);
    health.Overall.Should().Be(Sora.Core.HealthState.Healthy);

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

    public sealed record TestEntity(string Id) : Sora.Data.Abstractions.IEntity<string>
    {
        public string? Name { get; init; }
        public int Age { get; init; }
    }
}
