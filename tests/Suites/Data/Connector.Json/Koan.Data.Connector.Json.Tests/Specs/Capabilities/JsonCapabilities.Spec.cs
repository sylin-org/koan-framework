using System;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Json.Tests.Support;

namespace Koan.Data.Connector.Json.Tests.Specs.Capabilities;

public sealed class JsonCapabilitiesSpec
{
    private readonly ITestOutputHelper _output;

    public JsonCapabilitiesSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Repository_reports_linq_capability_and_no_native_bulk_writes()
    {
        await TestPipeline.For<JsonCapabilitiesSpec>(_output, nameof(Repository_reports_linq_capability_and_no_native_bulk_writes))
            .Using<JsonConnectorFixture>("fixture", static ctx => JsonConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                await fixture.ResetAsync<CapabilityProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                fixture.BindHost();

                var repository = fixture.Data.GetRepository<CapabilityProbe, string>();
                repository.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>();

                // ARCH-0084: negotiate via the unified CapabilitySet. JSON advertises no native bulk writes.
                var caps = DataCaps.Describe(repository, repository.GetType().Name);
                caps.Has(DataCaps.Query.Linq).Should().BeTrue();
                caps.Has(DataCaps.Query.String).Should().BeFalse();
                caps.Has(DataCaps.Write.BulkUpsert).Should().BeFalse();
                caps.Has(DataCaps.Write.BulkDelete).Should().BeFalse();
                caps.Has(DataCaps.Write.AtomicBatch).Should().BeFalse();
                caps.Has(DataCaps.Write.FastRemove).Should().BeFalse();

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
