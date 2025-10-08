using System;
using Koan.Data.Abstractions;
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
            .Using<JsonConnectorFixture>("fixture", static ctx => JsonConnectorFixture.CreateAsync(ctx))
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
                repository.Should().BeAssignableTo<ILinqQueryRepository<CapabilityProbe, string>>();
                repository.Should().BeAssignableTo<ILinqQueryRepositoryWithOptions<CapabilityProbe, string>>();
                repository.Should().BeAssignableTo<IDataRepositoryWithOptions<CapabilityProbe, string>>();

                var queryCaps = repository.Should().BeAssignableTo<IQueryCapabilities>().Subject;
                queryCaps.Capabilities.Should().Be(QueryCapabilities.Linq);

                var writeCaps = repository.Should().BeAssignableTo<IWriteCapabilities>().Subject;
                writeCaps.Writes.Should().Be(WriteCapabilities.None);

                await CapabilityProbe.UpsertAsync(new CapabilityProbe { Name = "cap" });
                var all = await CapabilityProbe.All();
                all.Should().ContainSingle(p => p.Name == "cap");
            })
            .RunAsync();
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = string.Empty;
    }
}
