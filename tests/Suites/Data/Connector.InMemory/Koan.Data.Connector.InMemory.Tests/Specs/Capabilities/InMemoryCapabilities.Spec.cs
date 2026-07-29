using System;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Capabilities;

public sealed class InMemoryCapabilitiesSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact]
    public async Task Repository_reports_only_realized_query_and_write_capabilities()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        var data = host.Services.GetRequiredService<IDataService>();
        var repo = data.GetRepository<CapabilityProbe, string>();
        var queryRepo = repo.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>().Which;

        // ARCH-0084: negotiate via the unified CapabilitySet (verifies the facade forwards
        // the inner adapter's declaration through IDescribesCapabilities).
        var caps = DataCaps.Describe(repo, repo.GetType().Name);
        caps.Has(DataCaps.Query.Linq).Should().BeTrue();
        caps.Has(DataCaps.Query.String).Should().BeFalse();
        caps.Has(DataCaps.Write.BulkUpsert).Should().BeTrue();
        caps.Has(DataCaps.Write.BulkDelete).Should().BeTrue();
        caps.Has(DataCaps.Write.AtomicBatch).Should().BeFalse();

        var published = new ClaimCapture();
        new InMemoryAdapterFactory().DescribeClaims(published);
        published.Capabilities.Should().Contain([
            DataCaps.Query.Linq,
            DataCaps.Query.Filter,
            DataCaps.Write.BulkUpsert,
            DataCaps.Write.BulkDelete,
            DataCaps.Isolation.RowScoped,
            DataCaps.Isolation.ContainerScoped,
            DataCaps.Isolation.DatabaseScoped
        ]);
        published.Capabilities.Should().NotContain(DataCaps.Write.AtomicBatch);

        var batch = CapabilityProbe.Batch();
        batch.ExecutionCapabilities.Should().Be(BatchExecutionCapabilities.None);
        batch.Add(new CapabilityProbe { Name = "must-not-commit" });
        await FluentActions.Invoking(() => batch.Save(new BatchOptions(RequireAtomic: true)))
            .Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*does not expose a proved native atomic batch boundary*");
        (await CapabilityProbe.Count.Exact()).Should().Be(0);

        await CapabilityProbe.Upsert(new CapabilityProbe { Name = "cap" });

        var pageOnly = await queryRepo.Query(
            QueryDefinition.All.WithPagination(1, 1).WithCountStrategy(null));
        pageOnly.TotalCount.Should().BeNull("numbered paging alone does not request a total");

        var countedPage = await queryRepo.Query(
            QueryDefinition.All.WithPagination(1, 1).WithCountStrategy(CountStrategy.Exact));
        countedPage.TotalCount.Should().Be(1);

        var count = await CapabilityProbe.Count.Exact();
        count.Should().Be(1);
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = "";
    }

    private sealed class ClaimCapture : IDataClaims
    {
        public HashSet<Capability> Capabilities { get; } = [];
        public IDataClaims Profile(string profile, string? qualifier = null, bool advertised = true) => this;
        public IDataClaims Capability(Capability capability, bool advertised = true)
        {
            if (advertised) Capabilities.Add(capability);
            return this;
        }
    }
}
