using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Json.Tests.Specs;

public sealed class JsonPolicyAndMappingSpec(JsonFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<JsonFixture>(fixture, output)
{
    [Fact]
    public async Task Explicit_physical_mapping_rejects_instead_of_being_ignored()
    {
        RequireBackingStore();
        var settings = new Dictionary<string, string?>(Fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Mapped:Adapter"] = "json"
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
    public async Task External_missing_directory_rejects_without_creating_it()
    {
        RequireBackingStore();
        var missing = Path.Combine(Path.GetTempPath(), $"koan-json-external-{Guid.CreateVersion7():N}");
        var settings = new Dictionary<string, string?>(Fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:ExternalJson:Adapter"] = "json",
            ["Koan:Data:Sources:ExternalJson:json:DirectoryPath"] = missing,
            ["Koan:Data:Sources:ExternalJson:StorageLifecycle"] = StorageLifecycle.External.ToString()
        };
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("ExternalJson"))
            await FluentActions.Invoking(() => ExternalItem.All())
                .Should().ThrowAsync<DirectoryNotFoundException>();
        Directory.Exists(missing).Should().BeFalse();
    }

    [Fact]
    public async Task Read_only_write_rejects_without_creating_an_entity_file()
    {
        RequireBackingStore();
        var settings = new Dictionary<string, string?>(Fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Frozen:Adapter"] = "json",
            ["Koan:Data:Sources:Frozen:Access"] = DataSourceAccess.ReadOnly.ToString()
        };
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("Frozen"))
            await FluentActions.Invoking(() => new FrozenItem { Name = "denied" }.Save())
                .Should().ThrowAsync<DataSourcePolicyException>();
        Directory.EnumerateFiles(Fixture.RootPath, "*.json").Should().BeEmpty();
    }

    public sealed class MappedItem : Entity<MappedItem>
    {
        public string Name { get; set; } = "";
    }

    public sealed class ExternalItem : Entity<ExternalItem>;

    public sealed class FrozenItem : Entity<FrozenItem>
    {
        public string Name { get; set; } = "";
    }
}
