using System;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Redis.Tests.Support;

namespace Koan.Data.Connector.Redis.Tests.Specs.Capabilities;

public sealed class RedisCapabilitiesSpec
{
    private readonly ITestOutputHelper _output;

    public RedisCapabilitiesSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Repository_reports_linq_and_fast_remove_capabilities()
    {
        await TestPipeline.For<RedisCapabilitiesSpec>(_output, nameof(Repository_reports_linq_and_fast_remove_capabilities))
            .RequireDocker()
            .UsingRedisContainer()
            .Using<RedisConnectorFixture>("fixture", static ctx => RedisConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<RedisConnectorFixture>("fixture");
                await fixture.ResetAsync<CapabilityProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<RedisConnectorFixture>("fixture");
                fixture.BindHost();

                var repository = fixture.Data.GetRepository<CapabilityProbe, string>();
                repository.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>();

                // ARCH-0084: negotiate via the unified CapabilitySet.
                var caps = DataCaps.Describe(repository, repository.GetType().Name);
                caps.Has(DataCaps.Query.Linq).Should().BeTrue();
                caps.Has(DataCaps.Query.String).Should().BeFalse();
                caps.Has(DataCaps.Write.FastRemove).Should().BeTrue();
                caps.Has(DataCaps.Retention.TtlIndex).Should().BeTrue(); // DATA-0101 native key TTL
                caps.Has(DataCaps.Write.BulkUpsert).Should().BeFalse();
                caps.Has(DataCaps.Write.BulkDelete).Should().BeFalse();
                caps.Has(DataCaps.Write.AtomicBatch).Should().BeFalse();

                await CapabilityProbe.Upsert(new CapabilityProbe { Name = "cap" });
                var all = await CapabilityProbe.All();
                all.Should().ContainSingle(p => p.Name == "cap");
            })
            .Run();
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = "";
    }
}
