using Koan.Core;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

public sealed class MongoLegacyMappingSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task Flat_object_nested_and_composite_legacy_shapes_use_the_entity_surface()
    {
        RequireBackingStore();
        var suffix = Guid.NewGuid().ToString("N");
        var customers = $"legacy_customers_{suffix}";
        var paths = $"legacy_paths_{suffix}";
        var sites = $"legacy_sites_{suffix}";
        var database = new MongoClient(Fixture.ConnectionString).GetDatabase(Fixture.Database);
        await database.GetCollection<BsonDocument>(customers).InsertOneAsync(new BsonDocument
        {
            ["CUSTOMER_NO"] = 7L,
            ["DISPLAY_NM"] = "Ada Lovelace",
            ["PROFILE"] = new BsonDocument
            {
                ["PreferredLanguage"] = "en",
                ["Tags"] = new BsonArray(["pioneer", "vip"])
            },
            ["LEGACY_ONLY"] = "preserve-me"
        });
        await database.GetCollection<BsonDocument>(paths).InsertOneAsync(new BsonDocument
        {
            ["CUSTOMER_NO"] = 9L,
            ["NAME_DATA"] = new BsonDocument
            {
                ["full"] = "Grace Hopper",
                ["first"] = "Grace",
                ["legacy"] = "preserve-me"
            }
        });
        await database.GetCollection<BsonDocument>(sites).InsertOneAsync(new BsonDocument
        {
            ["CUSTOMER_NO"] = 42L,
            ["SITE_NO"] = 3,
            ["DISPLAY_NM"] = "Primary"
        });

        var settings = ExternalSettings("Legacy");
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                koan.Data.Source("Legacy").Map<LegacyCustomer>(map => map
                    .Container(customers)
                    .Key(customer => customer.Id).Name("CUSTOMER_NO")
                    .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
                    .Property(customer => customer.Profile).Object("PROFILE"));
                koan.Data.Source("Legacy").Map<FlatLegacyCustomer>(map => map
                    .Container(paths)
                    .Key(customer => customer.Id).Name("CUSTOMER_NO")
                    .Property(customer => customer.NameFull).Path("NAME_DATA", "full")
                    .Property(customer => customer.NameFirst).Path("NAME_DATA", "first"));
                koan.Data.Source("Legacy").Map<MappedSite>(map => map
                    .Container(sites)
                    .Key(site => site.Id).Parts(parts => parts
                        .Property(key => key.CustomerNo).Name("CUSTOMER_NO")
                        .Property(key => key.SiteNo).Name("SITE_NO"))
                    .Property(site => site.DisplayName).Name("DISPLAY_NM"));
            }))
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("Legacy"))
        {
            var customer = await LegacyCustomer.Get(7);
            customer!.Name.Full.Should().Be("Ada Lovelace");
            customer.Profile.Tags.Should().Equal("pioneer", "vip");
            (await LegacyCustomer.Query(item => item.Name.Full == "Ada Lovelace"))
                .Should().ContainSingle();
            customer.Name.Full = "Augusta Ada King";
            customer.Profile.PreferredLanguage = "fr";
            await customer.Save();

            var flat = await FlatLegacyCustomer.Get(9);
            flat!.NameFull.Should().Be("Grace Hopper");
            flat.NameFull = "Rear Admiral Grace Hopper";
            await flat.Save();

            var site = await MappedSite.Get(new CustomerSiteId(42, 3));
            site!.DisplayName.Should().Be("Primary");
        }

        var storedCustomer = await database.GetCollection<BsonDocument>(customers)
            .Find(new BsonDocument("CUSTOMER_NO", 7L)).SingleAsync();
        storedCustomer["DISPLAY_NM"].AsString.Should().Be("Augusta Ada King");
        storedCustomer["PROFILE"]["PreferredLanguage"].AsString.Should().Be("fr");
        storedCustomer["LEGACY_ONLY"].AsString.Should().Be("preserve-me");

        var storedPath = await database.GetCollection<BsonDocument>(paths)
            .Find(new BsonDocument("CUSTOMER_NO", 9L)).SingleAsync();
        storedPath["NAME_DATA"]["full"].AsString.Should().Be("Rear Admiral Grace Hopper");
        storedPath["NAME_DATA"]["first"].AsString.Should().Be("Grace");
        storedPath["NAME_DATA"]["legacy"].AsString.Should().Be("preserve-me");
    }

    [Fact]
    public async Task Read_only_external_map_reads_and_rejects_writes_before_mutation()
    {
        RequireBackingStore();
        var collectionName = $"read_only_customers_{Guid.NewGuid():N}";
        var collection = new MongoClient(Fixture.ConnectionString)
            .GetDatabase(Fixture.Database)
            .GetCollection<BsonDocument>(collectionName);
        await collection.InsertOneAsync(new BsonDocument
        {
            ["CUSTOMER_NO"] = 11L,
            ["DISPLAY_NM"] = "Source owned"
        });

        var settings = ExternalSettings("ReadOnlyLegacy");
        settings["Koan:Data:Sources:ReadOnlyLegacy:Access"] = DataSourceAccess.ReadOnly.ToString();
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("ReadOnlyLegacy").Map<ReadOnlyCustomer>(map => map
                    .Container(collectionName)
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

        var stored = await collection.Find(new BsonDocument("CUSTOMER_NO", 11L)).SingleAsync();
        stored["DISPLAY_NM"].AsString.Should().Be("Source owned");
    }

    [Fact]
    public async Task Managed_mapping_realizes_indexes_on_physical_paths()
    {
        RequireBackingStore();
        var collectionName = $"mapped_indexes_{Guid.NewGuid():N}";
        var settings = new Dictionary<string, string?>(Fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Mapped:Adapter"] = "mongo",
            ["Koan:Data:Sources:Mapped:ConnectionString"] = Fixture.ConnectionString,
            ["Koan:Data:Sources:Mapped:Database"] = Fixture.Database
        };
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("Mapped").Map<IndexedCustomer>(map => map
                    .Container(collectionName)
                    .Key(customer => customer.Id).Name("CUSTOMER_NO")
                    .Property(customer => customer.DisplayName).Name("DISPLAY_NM")
                    .Property(customer => customer.ExpiresAt).Name("EXPIRES_AT"))))
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("Mapped"))
            await new IndexedCustomer
            {
                DisplayName = "Indexed",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            }.Save();

        var collection = new MongoClient(Fixture.ConnectionString)
            .GetDatabase(Fixture.Database)
            .GetCollection<BsonDocument>(collectionName);
        var document = await collection.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        document["EXPIRES_AT"].BsonType.Should().Be(BsonType.DateTime);
        using var cursor = await collection.Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();
        indexes.Should().Contain(index =>
            index["name"].AsString == "ix_display_name" && index["key"].AsBsonDocument.Contains("DISPLAY_NM"));
        indexes.Should().Contain(index =>
            index["name"].AsString == "ix_expiry" &&
            index["key"].AsBsonDocument.Contains("EXPIRES_AT") &&
            index["expireAfterSeconds"].ToInt64() == 0);
    }

    private Dictionary<string, string?> ExternalSettings(string source) =>
        new(Fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            [$"Koan:Data:Sources:{source}:Adapter"] = "mongo",
            [$"Koan:Data:Sources:{source}:ConnectionString"] = Fixture.ConnectionString,
            [$"Koan:Data:Sources:{source}:Database"] = Fixture.Database,
            [$"Koan:Data:Sources:{source}:StorageLifecycle"] = StorageLifecycle.External.ToString(),
            [$"Koan:Data:Sources:{source}:Access"] = DataSourceAccess.ReadWrite.ToString()
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

    public readonly record struct CustomerSiteId(long CustomerNo, short SiteNo);

    public sealed class MappedSite : Entity<MappedSite, CustomerSiteId>
    {
        public string DisplayName { get; set; } = "";
    }

    public sealed class ReadOnlyCustomer : Entity<ReadOnlyCustomer, long>
    {
        public string DisplayName { get; set; } = "";
    }

    public sealed class IndexedCustomer : Entity<IndexedCustomer>
    {
        [Index(Name = "ix_display_name")]
        public string DisplayName { get; set; } = "";

        [Index(Name = "ix_expiry", Ttl = true)]
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
