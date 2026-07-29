using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory.Tests.Specs;

public sealed class InMemoryPolicyAndMappingSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact]
    public async Task Explicit_physical_mapping_rejects_instead_of_being_ignored()
    {
        RequireBackingStore();
        var settings = new Dictionary<string, string?>(Fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Mapped:Adapter"] = "inmemory"
        };
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("Mapped").Map<MappedItem>(map => map
                    .Container("LEGACY_ITEMS")
                    .Key(item => item.Id).Name("ITEM_NO")
                    .Property(item => item.Name).Name("DISPLAY_NM"))))
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("Mapped"))
            await FluentActions.Invoking(() => MappedItem.Get("one"))
                .Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*does not expose a physical compatibility-mapping surface*");
    }

    [Fact]
    public async Task External_lifecycle_rejects_before_creating_an_ephemeral_store()
    {
        RequireBackingStore();
        await using var host = await BootAsync(new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:ExternalMemory:Adapter"] = "inmemory",
            ["Koan:Data:Sources:ExternalMemory:StorageLifecycle"] = StorageLifecycle.External.ToString()
        });

        using (EntityContext.Source("ExternalMemory"))
            await FluentActions.Invoking(() => ExternalItem.All())
                .Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*cannot open source 'ExternalMemory' as External*");
    }

    public sealed class MappedItem : Entity<MappedItem>
    {
        public string Name { get; set; } = "";
    }

    public sealed class ExternalItem : Entity<ExternalItem>;
}
