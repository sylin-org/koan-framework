using System;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core.Model;
using Koan.Data.Connector.InMemory.Tests.Support;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Capabilities;

public sealed class InMemoryCapabilitiesSpec
{
    private readonly ITestOutputHelper _output;

    public InMemoryCapabilitiesSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Repository_reports_linq_and_atomic_write_capabilities()
    {
        await TestPipeline.For<InMemoryCapabilitiesSpec>(_output, nameof(Repository_reports_linq_and_atomic_write_capabilities))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<CapabilityProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                fixture.BindHost();

                var repo = fixture.Data.GetRepository<CapabilityProbe, string>();
                repo.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>();

                // ARCH-0084: negotiate via the unified CapabilitySet (verifies the facade forwards
                // the inner adapter's declaration through IDescribesCapabilities).
                var caps = DataCaps.Describe(repo, repo.GetType().Name);
                caps.Has(DataCaps.Query.Linq).Should().BeTrue();
                caps.Has(DataCaps.Query.String).Should().BeFalse();
                caps.Has(DataCaps.Write.BulkUpsert).Should().BeTrue();
                caps.Has(DataCaps.Write.BulkDelete).Should().BeTrue();
                caps.Has(DataCaps.Write.AtomicBatch).Should().BeTrue();

                await CapabilityProbe.Upsert(new CapabilityProbe { Name = "cap" });
                var count = await CapabilityProbe.Count.Exact();
                count.Should().Be(1);
            })
            .Run();
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = "";
    }
}
