using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

public sealed class SqliteLegacyMappingSpec(SqliteFixture fixture)
{
    [Fact]
    public async Task Flat_and_structured_legacy_shape_uses_the_ordinary_entity_surface()
    {
        await Seed();
        var settings = new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Legacy:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Legacy:ConnectionString"] = fixture.ConnectionString,
            ["Koan:Data:Sources:Legacy:StorageLifecycle"] = StorageLifecycle.External.ToString(),
            ["Koan:Data:Sources:Legacy:Access"] = DataSourceAccess.ReadWrite.ToString()
        };
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("Legacy").Map<LegacyCustomer>(map => map
                    .Container("CUSTOMER")
                    .Key(customer => customer.Id).Name("CUSTOMER_NO")
                    .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
                    .Property(customer => customer.Profile).Object("PROFILE_JSON"))))
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("Legacy"))
        {
            var customer = await LegacyCustomer.Get(7);
            customer.Should().NotBeNull();
            customer!.Name.Full.Should().Be("Ada Lovelace");
            customer.Profile.PreferredLanguage.Should().Be("en");
            customer.Profile.Tags.Should().Equal("pioneer", "vip");

            var found = await LegacyCustomer.Query(item => item.Name.Full == "Ada Lovelace");
            found.Should().ContainSingle().Which.Id.Should().Be(7);

            customer.Name.Full = "Augusta Ada King";
            customer.Profile.PreferredLanguage = "fr";
            await customer.Save();
        }

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISPLAY_NM, PROFILE_JSON FROM CUSTOMER WHERE CUSTOMER_NO = 7";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("Augusta Ada King");
        reader.GetString(1).Should().Contain("\"PreferredLanguage\":\"fr\"");
    }

    [Fact]
    public async Task Nested_physical_paths_preserve_unmapped_legacy_values()
    {
        await using (var connection = new SqliteConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS CUSTOMER_PATHS (
                    CUSTOMER_NO INTEGER NOT NULL PRIMARY KEY,
                    NAME_DATA TEXT NOT NULL
                );
                DELETE FROM CUSTOMER_PATHS;
                INSERT INTO CUSTOMER_PATHS (CUSTOMER_NO, NAME_DATA)
                VALUES (9, '{"full":"Grace Hopper","first":"Grace","legacy":"preserve-me"}');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var settings = LegacySettings();
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("Legacy").Map<FlatLegacyCustomer>(map => map
                    .Container("CUSTOMER_PATHS")
                    .Key(customer => customer.Id).Name("CUSTOMER_NO")
                    .Property(customer => customer.NameFull).Path("NAME_DATA", "full")
                    .Property(customer => customer.NameFirst).Path("NAME_DATA", "first"))))
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("Legacy"))
        {
            var customer = await FlatLegacyCustomer.Get(9);
            customer!.NameFull.Should().Be("Grace Hopper");
            customer.NameFirst.Should().Be("Grace");
            customer.NameFull = "Rear Admiral Grace Hopper";
            await customer.Save();
        }

        await using var verify = new SqliteConnection(fixture.ConnectionString);
        await verify.OpenAsync();
        await using var read = verify.CreateCommand();
        read.CommandText = "SELECT NAME_DATA FROM CUSTOMER_PATHS WHERE CUSTOMER_NO = 9";
        var json = (string)(await read.ExecuteScalarAsync())!;
        json.Should().Contain("\"full\":\"Rear Admiral Grace Hopper\"");
        json.Should().Contain("\"first\":\"Grace\"");
        json.Should().Contain("\"legacy\":\"preserve-me\"");
    }

    [Fact]
    public async Task Managed_maps_support_identity_object_composite_and_generated_shapes()
    {
        var settings = new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Mapped:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Mapped:ConnectionString"] = fixture.ConnectionString
        };
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                koan.Data.Source("Mapped").Map<MappedEnvelope>(map => map
                    .Container("MAPPED_ENVELOPES")
                    .Key(item => item.Id).Name("ID")
                    .Object("DATA"));
                koan.Data.Source("Mapped").Map<MappedSite>(map => map
                    .Container("MAPPED_SITES")
                    .Key(item => item.Id).Parts(parts => parts
                        .Property(key => key.CustomerNo).Name("CUSTOMER_NO")
                        .Property(key => key.SiteNo).Name("SITE_NO"))
                    .Property(item => item.DisplayName).Name("DISPLAY_NM"));
                koan.Data.Source("Mapped").Map<GeneratedCustomer>(map => map
                    .Container("GENERATED_CUSTOMERS")
                    .Key(item => item.Id).Name("ID").Generated()
                    .Property(item => item.DisplayName).Name("DISPLAY_NM"));
            }))
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("Mapped"))
        {
            var envelope = await new MappedEnvelope { Value = "whole object" }.Save();
            (await MappedEnvelope.Get(envelope.Id))!.Value.Should().Be("whole object");

            var site = await new MappedSite
            {
                Id = new CustomerSiteId(42, 3),
                DisplayName = "Primary"
            }.Save();
            (await MappedSite.Get(site.Id))!.DisplayName.Should().Be("Primary");

            var generated = await new GeneratedCustomer { DisplayName = "Generated" }.Save();
            generated.Id.Should().BeGreaterThan(0);
            generated.DisplayName = "Updated";
            await generated.Save();
            (await GeneratedCustomer.Get(generated.Id))!.DisplayName.Should().Be("Updated");

            using (EntityContext.Partition("must-not-alias"))
                await FluentActions.Invoking(() => MappedEnvelope.Get(envelope.Id))
                    .Should().ThrowAsync<NotSupportedException>();
        }
    }

    [Fact]
    public async Task Read_only_external_map_reads_normally_and_rejects_write_before_mutation()
    {
        await using (var connection = new SqliteConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS READ_ONLY_CUSTOMER (
                    CUSTOMER_NO INTEGER NOT NULL PRIMARY KEY,
                    DISPLAY_NM TEXT NOT NULL
                );
                DELETE FROM READ_ONLY_CUSTOMER;
                INSERT INTO READ_ONLY_CUSTOMER (CUSTOMER_NO, DISPLAY_NM) VALUES (11, 'Source owned');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var settings = new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:ReadOnlyLegacy:Adapter"] = "sqlite",
            ["Koan:Data:Sources:ReadOnlyLegacy:ConnectionString"] = fixture.ConnectionString,
            ["Koan:Data:Sources:ReadOnlyLegacy:StorageLifecycle"] = StorageLifecycle.External.ToString(),
            ["Koan:Data:Sources:ReadOnlyLegacy:Access"] = DataSourceAccess.ReadOnly.ToString()
        };
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("ReadOnlyLegacy").Map<ReadOnlyCustomer>(map => map
                    .Container("READ_ONLY_CUSTOMER")
                    .Key(customer => customer.Id).Name("CUSTOMER_NO")
                    .Property(customer => customer.DisplayName).Name("DISPLAY_NM"))))
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("ReadOnlyLegacy"))
        {
            var customer = await ReadOnlyCustomer.Get(11);
            customer!.DisplayName.Should().Be("Source owned");
            customer.DisplayName = "must not persist";
            await FluentActions.Invoking(() => customer.Save())
                .Should().ThrowAsync<DataSourcePolicyException>();
        }

        await using var verify = new SqliteConnection(fixture.ConnectionString);
        await verify.OpenAsync();
        await using var read = verify.CreateCommand();
        read.CommandText = "SELECT DISPLAY_NM FROM READ_ONLY_CUSTOMER WHERE CUSTOMER_NO = 11";
        (await read.ExecuteScalarAsync()).Should().Be("Source owned");
    }

    private async Task Seed()
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CUSTOMER (
                CUSTOMER_NO INTEGER NOT NULL PRIMARY KEY,
                DISPLAY_NM TEXT NOT NULL,
                PROFILE_JSON TEXT NOT NULL
            );
            DELETE FROM CUSTOMER;
            INSERT INTO CUSTOMER (CUSTOMER_NO, DISPLAY_NM, PROFILE_JSON)
            VALUES (7, 'Ada Lovelace', '{"PreferredLanguage":"en","Tags":["pioneer","vip"]}');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private Dictionary<string, string?> LegacySettings() =>
        new(fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Legacy:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Legacy:ConnectionString"] = fixture.ConnectionString,
            ["Koan:Data:Sources:Legacy:StorageLifecycle"] = StorageLifecycle.External.ToString(),
            ["Koan:Data:Sources:Legacy:Access"] = DataSourceAccess.ReadWrite.ToString()
        };

    public sealed class LegacyCustomer : Entity<LegacyCustomer, long>
    {
        public CustomerName Name { get; set; } = new();
        public CustomerProfile Profile { get; set; } = new();
    }

    public sealed class CustomerName
    {
        public string Full { get; set; } = "";
    }

    public sealed class CustomerProfile
    {
        public string? PreferredLanguage { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = [];
    }

    public sealed class FlatLegacyCustomer : Entity<FlatLegacyCustomer, long>
    {
        public string NameFull { get; set; } = "";
        public string NameFirst { get; set; } = "";
    }

    public sealed class MappedEnvelope : Entity<MappedEnvelope>
    {
        public string Value { get; set; } = "";
    }

    public readonly record struct CustomerSiteId(long CustomerNo, short SiteNo);

    public sealed class MappedSite : Entity<MappedSite, CustomerSiteId>
    {
        public string DisplayName { get; set; } = "";
    }

    public sealed class GeneratedCustomer : Entity<GeneratedCustomer, long>
    {
        public string DisplayName { get; set; } = "";
    }

    public sealed class ReadOnlyCustomer : Entity<ReadOnlyCustomer, long>
    {
        public string DisplayName { get; set; } = "";
    }
}
