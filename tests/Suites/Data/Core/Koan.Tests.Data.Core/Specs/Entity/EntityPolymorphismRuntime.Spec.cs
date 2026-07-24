using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Polymorphism;
using Koan.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Media = Koan.Tests.Data.Core.Support.PolymorphicEntityTestMedia;
using Anime = Koan.Tests.Data.Core.Support.PolymorphicEntityTestMedia.Anime;
using DirectAnime = Koan.Tests.Data.Core.Support.PolymorphicEntityTestMedia.DirectAnime;
using Manga = Koan.Tests.Data.Core.Support.PolymorphicEntityTestMedia.Manga;

namespace Koan.Tests.Data.Core.Specs.Entity;

public sealed class EntityPolymorphismRuntimeSpec
{
    private const string UnknownTypeId = "Missing.Assembly:Missing.Entity";

    [Fact]
    public void Descriptor_identifies_the_entity_root()
    {
        var descriptor = EntityRootDescriptor.For(typeof(Media));

        descriptor.DeclaredType.Should().Be(typeof(Media));
        descriptor.RootType.Should().Be(typeof(Media));
        descriptor.KeyType.Should().Be(typeof(string));
        descriptor.VariantType.Should().BeNull();
        descriptor.IsRoot.Should().BeTrue();
        descriptor.IsVariant.Should().BeFalse();
    }

    [Fact]
    public void Descriptor_identifies_a_self_closed_generated_variant()
    {
        var descriptor = EntityRootDescriptor.For(typeof(Anime));

        descriptor.DeclaredType.Should().Be(typeof(Anime));
        descriptor.RootType.Should().Be(typeof(Media));
        descriptor.KeyType.Should().Be(typeof(string));
        descriptor.VariantType.Should().Be(typeof(Anime));
        descriptor.IsRoot.Should().BeFalse();
        descriptor.IsVariant.Should().BeTrue();
    }

    [Fact]
    public void Descriptor_rejects_direct_concrete_inheritance_with_the_family_correction()
    {
        var act = () => EntityRootDescriptor.For(typeof(DirectAnime));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DirectAnime*")
            .WithMessage("*PolymorphicEntityTestMedia*")
            .WithMessage("*DirectAnime : PolymorphicEntityTestMedia<DirectAnime>*");
    }

    [Fact]
    public void Json_write_adds_the_source_type_for_every_member_of_a_known_family()
    {
        var root = Write(new Media { Kind = "Media" });
        var variant = Write(new Anime { Kind = "Anime", Episodes = 26 });

        root.Value<string>(EntityFamilyStorage.TypeField)
            .Should().Be(EntityTypeCatalog.TypeId(typeof(Media)));
        variant.Value<string>(EntityFamilyStorage.TypeField)
            .Should().Be(EntityTypeCatalog.TypeId(typeof(Anime)));
        variant.Value<int?>(nameof(Anime.Episodes)).Should().Be(26);
    }

    [Fact]
    public void Ordinary_entity_reads_keep_the_nominal_streaming_path()
    {
        EntityJsonConverter.Instance.CanConvert(typeof(PlainEntity)).Should().BeFalse();
    }

    [Fact]
    public void Json_member_mapping_cannot_claim_the_reserved_type_field()
    {
        var act = () => Write(new JsonMappedCollision { UserField = "spoof" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JsonMappedCollision*")
            .WithMessage("*UserField*")
            .WithMessage($"*{EntityFamilyStorage.TypeField}*");
    }

    [Fact]
    public void Json_extension_data_cannot_claim_the_reserved_type_field()
    {
        var model = new JsonExtensionCollision();
        model.ExtensionData[EntityFamilyStorage.TypeField] = "spoof";

        var act = () => Write(model);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*extension data*")
            .WithMessage($"*{EntityFamilyStorage.TypeField}*");
    }

    [Fact]
    public void Null_json_extension_data_remains_serializable()
    {
        var act = () => Write(new JsonNullableExtensionData());

        act.Should().NotThrow();
    }

    [Fact]
    public void Root_read_restores_the_exact_runtime_variant()
    {
        var document = Write(new Anime { Kind = "Anime", Episodes = 26 });

        var materialized = Read<Media>(document)
            .Should().BeOfType<Anime>().Which;

        materialized.Kind.Should().Be("Anime");
        materialized.Episodes.Should().Be(26);
    }

    [Fact]
    public void Typed_leaf_read_returns_the_exact_variant()
    {
        var document = Write(new Anime { Kind = "Anime", Episodes = 26 });

        var materialized = Read<Anime>(document);

        materialized.Should().BeOfType<Anime>();
        materialized.Episodes.Should().Be(26);
    }

    [Fact]
    public void Root_read_then_resave_preserves_the_variant_field_and_source_type()
    {
        var stored = Write(new Anime { Kind = "Anime", Episodes = 26 });
        var loadedThroughRoot = Read<Media>(stored);

        var resaved = Write(loadedThroughRoot);

        resaved.Value<int?>(nameof(Anime.Episodes)).Should().Be(26);
        resaved.Value<string>(EntityFamilyStorage.TypeField)
            .Should().Be(EntityTypeCatalog.TypeId(typeof(Anime)));
    }

    [Fact]
    public void Nested_entity_family_values_restore_their_own_runtime_types()
    {
        var stored = Write(new Anime
        {
            Kind = "Anime",
            Episodes = 26,
            Related = new Manga { Kind = "Manga", Volumes = 6 }
        });

        var loaded = Read<Media>(stored)
            .Should().BeOfType<Anime>().Which;

        loaded.Related.Should().BeOfType<Manga>()
            .Which.Volumes.Should().Be(6);
    }

    [Fact]
    public void Explicit_target_does_not_leak_onto_a_nested_sibling_variant()
    {
        var stored = Write(new Anime
        {
            Kind = "Anime",
            Episodes = 26,
            Related = new Manga { Kind = "Manga", Volumes = 6 }
        });

        using var _ = EntityMaterializationScope.Enter(typeof(Media), typeof(Anime));
        var loaded = Read<Media>(stored)
            .Should().BeOfType<Anime>().Which;

        loaded.Related.Should().BeOfType<Manga>()
            .Which.Volumes.Should().Be(6);
    }

    [Fact]
    public void Explicit_target_does_not_leak_onto_a_nested_root_record()
    {
        var stored = Write(new Anime
        {
            Kind = "Anime",
            Episodes = 26,
            Related = new Media { Kind = "Plain" }
        });

        using var _ = EntityMaterializationScope.Enter(typeof(Media), typeof(Anime));
        var loaded = Read<Media>(stored)
            .Should().BeOfType<Anime>().Which;

        loaded.Related.Should().BeOfType<Media>()
            .Which.Should().NotBeOfType<Anime>();
        loaded.Related.Kind.Should().Be("Plain");
    }

    [Fact]
    public void Explicit_target_recovers_a_hintless_document_when_materialized_inside_the_scope()
    {
        var legacy = new JObject
        {
            [nameof(Media.Kind)] = "Anime",
            [nameof(Anime.Episodes)] = 13
        };

        // This is the Data Core materializer contract. An eager store that materializes its complete file before
        // a typed point-read scope exists cannot retroactively classify old hintless rows; those rows need migration.
        using var _ = EntityMaterializationScope.Enter(typeof(Media), typeof(Anime));
        var materialized = Read<Media>(legacy)
            .Should().BeOfType<Anime>().Which;

        materialized.Kind.Should().Be("Anime");
        materialized.Episodes.Should().Be(13);
    }

    [Fact]
    public void Root_read_rejects_an_unknown_source_type()
    {
        var document = Write(new Media { Kind = "Unknown" });
        document[EntityFamilyStorage.TypeField] = UnknownTypeId;

        var act = () => Read<Media>(document);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*not a registered concrete variant*")
            .WithMessage("*PolymorphicEntityTestMedia*");
    }

    [Fact]
    public void Stored_identity_wins_so_the_variant_repository_can_reject_a_wrong_top_level_type()
    {
        var document = Write(new Manga { Kind = "Manga", Volumes = 6 });

        using var _ = EntityMaterializationScope.Enter(typeof(Media), typeof(Anime));
        Read<Media>(document).Should().BeOfType<Manga>();
    }

    [Fact]
    public async Task Cached_variant_view_resolves_the_root_repository_for_each_current_source()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = NonIsolatingFakeAdapterFactory.ProviderId,
                ["Koan:Data:Sources:Archive:Adapter"] = NonIsolatingFakeAdapterFactory.ProviderId
            })
            .Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IConfiguration>(configuration);
        registrations.AddKoanDataCore();
        registrations.AddSingleton<IDataAdapterFactory, NonIsolatingFakeAdapterFactory>();

        using var services = registrations.BuildServiceProvider();
        var data = services.GetRequiredService<IDataService>();
        var variant = data.GetRepository<Anime, string>();
        var current = new Anime { Id = "current", Kind = "Anime", Episodes = 12 };
        var archived = new Anime { Id = "archived", Kind = "Anime", Episodes = 24 };

        await variant.Upsert(current);
        using (EntityContext.Source("Archive"))
        {
            await variant.Upsert(archived);
            (await variant.Get(current.Id)).Should().BeNull();
            (await variant.Get(archived.Id)).Should().BeSameAs(archived);
        }

        (await variant.Get(current.Id)).Should().BeSameAs(current);
        (await variant.Get(archived.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Variant_write_uses_the_runtime_write_plan_for_leaf_persistence_concerns()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = NonIsolatingFakeAdapterFactory.ProviderId
            })
            .Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IConfiguration>(configuration);
        registrations.AddKoanDataCore();
        registrations.AddSingleton<IDataAdapterFactory, NonIsolatingFakeAdapterFactory>();

        using var services = registrations.BuildServiceProvider();
        var variant = services.GetRequiredService<IDataService>().GetRepository<Anime, string>();
        var anime = new Anime { Kind = "Anime", Episodes = 12 };

        await variant.Upsert(anime);

        anime.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Variant_round_trip_uses_the_runtime_field_transform_plan()
    {
        var calls = new List<string>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = NonIsolatingFakeAdapterFactory.ProviderId
            })
            .Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IConfiguration>(configuration);
        registrations.AddSingleton<IFieldTransformContributor>(
            new VariantTransformContributor(calls));
        registrations.AddKoanDataCore();
        registrations.AddSingleton<IDataAdapterFactory, NonIsolatingFakeAdapterFactory>();

        using var services = registrations.BuildServiceProvider();
        var variant = services.GetRequiredService<IDataService>().GetRepository<Anime, string>();
        var anime = new Anime { Kind = "Anime", Episodes = 12 };

        await variant.Upsert(anime);
        var loaded = await variant.Get(anime.Id);

        loaded.Should().NotBeNull();
        calls.Should().Equal("write:Anime", "read:Anime");
    }

    private static JObject Write(object entity)
        => JObject.FromObject(entity, CreateSerializer());

    private static T Read<T>(JObject document)
        where T : class
        => document.ToObject<T>(CreateSerializer())
           ?? throw new InvalidDataException($"Could not materialize '{typeof(T).FullName}'.");

    private static JsonSerializer CreateSerializer()
        => JsonSerializer.Create(
            EntityJsonSerialization.Apply(new JsonSerializerSettings()));

    private sealed class PlainEntity : Koan.Data.Core.Model.Entity<PlainEntity>;

    private sealed class JsonMappedCollision : Koan.Data.Core.Model.Entity<JsonMappedCollision>
    {
        [JsonProperty(EntityFamilyStorage.TypeField)]
        public string? UserField { get; set; }
    }

    private sealed class JsonExtensionCollision : Koan.Data.Core.Model.Entity<JsonExtensionCollision>
    {
        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; } =
            new Dictionary<string, JToken>(StringComparer.Ordinal);
    }

    private sealed class JsonNullableExtensionData : Koan.Data.Core.Model.Entity<JsonNullableExtensionData>
    {
        [JsonExtensionData]
        public IDictionary<string, JToken>? ExtensionData { get; set; }
    }

    private sealed class VariantTransformContributor(ICollection<string> calls) : IFieldTransformContributor
    {
        public string Id => "variant-test";

        public IFieldTransform? Build(Type entityType)
            => entityType == typeof(Anime)
                ? new VariantTransform(calls)
                : null;
    }

    private sealed class VariantTransform(ICollection<string> calls) : IFieldTransform
    {
        public void ApplyOnWrite(object entity) => calls.Add($"write:{entity.GetType().Name}");
        public void ApplyOnRead(object entity) => calls.Add($"read:{entity.GetType().Name}");
    }
}
