using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Serialization;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Polymorphism;
using Newtonsoft.Json.Linq;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class JsonCacheSerializerPolymorphismSpec
{
    [Fact]
    public async Task Root_typed_round_trip_restores_the_runtime_variant()
    {
        CacheMedia source = new CacheAnime
        {
            Kind = "Anime",
            Episodes = 26
        };
        var serializer = new JsonCacheSerializer();

        var cached = await serializer.SerializeAsync<CacheMedia>(
            source,
            new CacheEntryOptions(),
            CancellationToken.None);

        var payload = JObject.Parse(
            cached.ToText() ?? throw new InvalidDataException("JSON cache payload was empty."));
        payload.Value<string>(EntityFamilyStorage.TypeField)
            .Should().Be(EntityTypeCatalog.TypeId(typeof(CacheAnime)));
        payload.GetValue(nameof(CacheAnime.Episodes), StringComparison.OrdinalIgnoreCase)!
            .Value<int>().Should().Be(26);

        var restored = await serializer.DeserializeAsync<CacheMedia>(
            cached,
            CancellationToken.None);

        var anime = restored.Should().BeOfType<CacheAnime>().Which;
        anime.Kind.Should().Be("Anime");
        anime.Episodes.Should().Be(26);
    }
}

internal class CacheMedia : Entity<CacheMedia>
{
    public string Kind { get; set; } = "";
}

internal abstract class CacheMedia<TVariant> :
    CacheMedia,
    IEntityFamilyVariant<CacheMedia, TVariant, string>
    where TVariant : CacheMedia<TVariant>;

internal sealed class CacheAnime : CacheMedia<CacheAnime>
{
    public int? Episodes { get; set; }
}
