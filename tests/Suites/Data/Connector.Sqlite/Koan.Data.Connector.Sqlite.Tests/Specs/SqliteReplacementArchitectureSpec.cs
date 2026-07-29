using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

public sealed class SqliteReplacementArchitectureSpec(SqliteFixture fixture)
{
    [Fact]
    public async Task Managed_and_explicit_maps_use_one_repository_execution_type()
    {
        var settings = new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Mapped:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Mapped:ConnectionString"] = fixture.ConnectionString
        };

        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("Mapped").Map<MappedRecord>(map => map
                    .Container("MAPPED_REPLACEMENT_ARCHITECTURE")
                    .Key(item => item.Id).Name("ID")
                    .Property(item => item.Value).Name("VALUE"))))
            .StartAsync(TestContext.Current.CancellationToken);

        var factory = host.Services.GetRequiredService<SqliteAdapterFactory>();
        var managed = factory.Create<ManagedRecord, string>(host.Services, "Default");
        var mapped = factory.Create<MappedRecord, string>(host.Services, "Mapped");

        managed.GetType().GetGenericTypeDefinition()
            .Should().Be(mapped.GetType().GetGenericTypeDefinition(),
                "managed and mapped storage are physical shapes of one relational execution path");
    }

    public sealed class ManagedRecord : Entity<ManagedRecord>
    {
        public string Value { get; set; } = "";
    }

    public sealed class MappedRecord : Entity<MappedRecord>
    {
        public string Value { get; set; } = "";
    }
}
