using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Abstractions;
using Koan.Data.Connector.Mongo.Initialization;
using Koan.Data.Core.Model;
using Koan.Data.Core.Polymorphism;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Polymorphism;

/// <summary>
/// Non-container BSON evidence for DATA-0109. Mongo translates the adapter-neutral Entity-family
/// identity at its wire boundary without re-enabling the driver's native <c>_t</c> contract.
/// </summary>
public sealed class MongoEntityPolymorphismWireShapeSpec
{
    static MongoEntityPolymorphismWireShapeSpec()
    {
        MongoDriverConfiguration.EnsureApplied();
        MongoEntityDiscriminatorConvention.EnsureRegistered(typeof(PolyMedia));
    }

    [Fact]
    public void Root_nominal_write_of_a_variant_uses_the_Koan_type_field()
    {
        PolyMedia model = new PolyAnime
        {
            Id = Guid.NewGuid().ToString(),
            Kind = "Anime",
            Title = "Cowboy Bebop",
            Episodes = 26
        };

        var document = SerializeAsRoot(model);

        document.Contains(EntityFamilyStorage.TypeField).Should().BeTrue();
        document[EntityFamilyStorage.TypeField].AsString
            .Should().Be(EntityTypeCatalog.TypeId(typeof(PolyAnime)));
        document[nameof(PolyAnime.Episodes).ToLowerInvariant()].AsInt32.Should().Be(26);
        document.Contains("_t").Should().BeFalse();
    }

    [Fact]
    public void Root_nominal_read_restores_the_exact_variant()
    {
        var document = SerializeAsRoot(new PolyAnime
        {
            Id = Guid.NewGuid().ToString(),
            Kind = "Anime",
            Title = "Frieren",
            Episodes = 28
        });

        var materialized = DeserializeAsRoot(document)
            .Should().BeOfType<PolyAnime>().Which;

        materialized.Title.Should().Be("Frieren");
        materialized.Episodes.Should().Be(28);
    }

    [Fact]
    public void Stored_identity_wins_for_a_conflicting_operation_target()
    {
        var document = SerializeAsRoot(new PolyManga
        {
            Id = Guid.NewGuid().ToString(),
            Kind = "Manga",
            Title = "Witch Hat Atelier",
            Volumes = 13,
            Chapters = 80
        });

        using var _ = EntityMaterializationScope.Enter(typeof(PolyMedia), typeof(PolyAnime));
        DeserializeAsRoot(document).Should().BeOfType<PolyManga>();
    }

    [Fact]
    public void Explicit_target_does_not_leak_onto_a_nested_sibling_variant()
    {
        var document = SerializeAsRoot(new PolyAnime
        {
            Id = Guid.NewGuid().ToString(),
            Kind = "Anime",
            Title = "Outer",
            Episodes = 12,
            Related = new PolyManga
            {
                Id = Guid.NewGuid().ToString(),
                Kind = "Manga",
                Title = "Nested",
                Volumes = 5,
                Chapters = 28
            }
        });

        using var _ = EntityMaterializationScope.Enter(typeof(PolyMedia), typeof(PolyAnime));
        var materialized = DeserializeAsRoot(document)
            .Should().BeOfType<PolyAnime>().Which;

        materialized.Related.Should().BeOfType<PolyManga>()
            .Which.Volumes.Should().Be(5);
    }

    [Fact]
    public void Explicit_target_does_not_leak_onto_a_nested_root_record()
    {
        var document = SerializeAsRoot(new PolyAnime
        {
            Id = Guid.NewGuid().ToString(),
            Kind = "Anime",
            Title = "Outer",
            Episodes = 12,
            Related = new PolyMedia
            {
                Id = Guid.NewGuid().ToString(),
                Kind = "Media",
                Title = "Nested root"
            }
        });

        using var _ = EntityMaterializationScope.Enter(typeof(PolyMedia), typeof(PolyAnime));
        var materialized = DeserializeAsRoot(document)
            .Should().BeOfType<PolyAnime>().Which;

        materialized.Related.Should().BeOfType<PolyMedia>()
            .Which.Should().NotBeOfType<PolyAnime>();
        materialized.Related.Title.Should().Be("Nested root");
    }

    [Fact]
    public void Root_record_uses_the_Koan_family_type_field()
    {
        var document = SerializeAsRoot(new PolyMedia
        {
            Id = Guid.NewGuid().ToString(),
            Kind = "Media",
            Title = "Shared title"
        });

        document[EntityFamilyStorage.TypeField].AsString
            .Should().Be(EntityTypeCatalog.TypeId(typeof(PolyMedia)));
        document.Contains("_t").Should().BeFalse();
    }

    [Fact]
    public void Bson_member_mapping_cannot_claim_the_reserved_type_field()
    {
        var act = () => MongoEntityDiscriminatorConvention.EnsureRegistered(typeof(BsonCollisionEntity));

        act.Should().Throw<BsonSerializationException>()
            .WithMessage("*UserField*")
            .WithMessage($"*{EntityFamilyStorage.TypeField}*");
    }

    [Fact]
    public void Bson_extra_elements_cannot_override_the_type_field()
    {
        EntityTypeCatalog.Register(typeof(BsonExtraAnime));
        MongoEntityDiscriminatorConvention.EnsureRegistered(typeof(BsonExtraMedia));
        BsonExtraMedia model = new BsonExtraAnime { Episodes = 12 };
        model.ExtraElements[EntityFamilyStorage.TypeField] = "spoof";

        var act = () => model.ToBsonDocument(typeof(BsonExtraMedia));

        act.Should().Throw<BsonSerializationException>()
            .WithMessage($"*{EntityFamilyStorage.TypeField}*");
    }

    [Fact]
    public void Identity_class_map_and_family_discriminator_are_composed_before_freeze()
    {
        EntityTypeCatalog.Register(typeof(PreMappedAnime));
        var map = new BsonClassMap(typeof(PreMappedMedia));
        map.AutoMap();
        map.SetIgnoreExtraElements(true);
        map.GetMemberMap(nameof(PreMappedMedia.Id))
            ?.SetSerializer(new SmartStringGuidSerializer());
        MongoEntityDiscriminatorConvention.ConfigureFamilyRootMap(map);
        BsonClassMap.RegisterClassMap(map);

        MongoEntityDiscriminatorConvention.EnsureRegistered(typeof(PreMappedMedia));
        PreMappedMedia model = new()
        {
            Id = Guid.NewGuid().ToString(),
            Kind = "Media"
        };
        var document = model.ToBsonDocument(typeof(PreMappedMedia));

        document[EntityFamilyStorage.TypeField].AsString
            .Should().Be(EntityTypeCatalog.TypeId(typeof(PreMappedMedia)));
    }

    private static BsonDocument SerializeAsRoot(PolyMedia model)
        => model.ToBsonDocument(typeof(PolyMedia));

    private static PolyMedia DeserializeAsRoot(BsonDocument document)
        => BsonSerializer.Deserialize<PolyMedia>(document);

    private sealed class BsonCollisionEntity : Entity<BsonCollisionEntity>
    {
        [BsonElement(EntityFamilyStorage.TypeField)]
        public string? UserField { get; set; }
    }

    private class BsonExtraMedia : Entity<BsonExtraMedia>
    {
        [BsonExtraElements]
        public BsonDocument ExtraElements { get; set; } = [];
    }

    private abstract class BsonExtraMedia<TVariant> :
        BsonExtraMedia,
        IEntityFamilyVariant<BsonExtraMedia, TVariant, string>
        where TVariant : BsonExtraMedia<TVariant>;

    private sealed class BsonExtraAnime : BsonExtraMedia<BsonExtraAnime>
    {
        public int? Episodes { get; set; }
    }

    private class PreMappedMedia : Entity<PreMappedMedia>
    {
        [Identifier]
        public override string Id { get; set; } = "";

        public string Kind { get; set; } = "";
    }

    private abstract class PreMappedMedia<TVariant> :
        PreMappedMedia,
        IEntityFamilyVariant<PreMappedMedia, TVariant, string>
        where TVariant : PreMappedMedia<TVariant>;

    private sealed class PreMappedAnime : PreMappedMedia<PreMappedAnime>
    {
        public int? Episodes { get; set; }
    }
}
