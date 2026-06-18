using Koan.Data.Abstractions.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Json.Tests.Specs.Capabilities;

public sealed class JsonCapabilitiesSpec(JsonFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<JsonFixture>(fixture, output)
{
    [Fact]
    public async Task Repository_reports_linq_capability_and_no_native_bulk_writes()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();

        var repository = data.GetRepository<CapabilityProbe, string>();
        repository.Should().BeAssignableTo<IQueryRepository<CapabilityProbe, string>>();

        // ARCH-0084: negotiate via the unified CapabilitySet. JSON advertises no native bulk writes.
        var caps = DataCaps.Describe(repository, repository.GetType().Name);
        caps.Has(DataCaps.Query.Linq).Should().BeTrue();
        caps.Has(DataCaps.Query.String).Should().BeFalse();
        caps.Has(DataCaps.Write.BulkUpsert).Should().BeFalse();
        caps.Has(DataCaps.Write.BulkDelete).Should().BeFalse();
        caps.Has(DataCaps.Write.AtomicBatch).Should().BeFalse();
        caps.Has(DataCaps.Write.FastRemove).Should().BeFalse();

        var partition = NewPartition("capabilities");
        using var lease = Lease(partition);

        await CapabilityProbe.Upsert(new CapabilityProbe { Name = "cap" });
        var all = await CapabilityProbe.All();
        all.Should().ContainSingle(p => p.Name == "cap");
    }

    private sealed class CapabilityProbe : Entity<CapabilityProbe>
    {
        public string Name { get; set; } = "";
    }
}
