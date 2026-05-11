using System.Text.Json;
using Koan.Tagging;

namespace Koan.Tests.Tagging.Unit.Specs;

public sealed class TagSetJsonSpec
{
    [Fact]
    public void Serialises_public_and_private_sides_as_nested_objects()
    {
        var t = new TagSet();
        t.Public["game"].Set("ffxiv");
        t.Public["technique"].Set(["dof", "clarity"]);
        t.Private["moderation"].Set("review-pending");

        var json = JsonSerializer.Serialize(t);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("public").GetProperty("game").EnumerateArray()
            .Select(e => e.GetString()).Should().BeEquivalentTo(["ffxiv"]);
        root.GetProperty("public").GetProperty("technique").EnumerateArray()
            .Select(e => e.GetString()).Should().BeEquivalentTo(["dof", "clarity"]);
        root.GetProperty("private").GetProperty("moderation").EnumerateArray()
            .Select(e => e.GetString()).Should().BeEquivalentTo(["review-pending"]);
    }

    [Fact]
    public void Empty_categories_are_stripped_from_json_output()
    {
        var t = new TagSet();
        var _ = t.Public["empty-cat"];                 // auto-create but leave empty
        t.Public["game"].Set("ffxiv");

        var json = JsonSerializer.Serialize(t);

        using var doc = JsonDocument.Parse(json);
        var pub = doc.RootElement.GetProperty("public");
        pub.TryGetProperty("empty-cat", out JsonElement _).Should().BeFalse();
        pub.TryGetProperty("game", out JsonElement _).Should().BeTrue();
    }

    [Fact]
    public void Round_trips_through_deserialise()
    {
        var t = new TagSet();
        t.Public["game"].Set(["ffxiv", "sims4"]);
        t.Public["technique"].Set("dof");
        t.Private["moderation"].Set("review-pending");

        var json = JsonSerializer.Serialize(t);
        var copy = JsonSerializer.Deserialize<TagSet>(json);

        copy.Should().NotBeNull();
        copy!.PublicTags.Should().BeEquivalentTo(["ffxiv", "sims4", "dof"]);
        copy.PrivateTags.Should().BeEquivalentTo(["review-pending"]);
        copy.Find("ffxiv")!.Category.Should().Be("game");
        copy.Find("review-pending")!.Scope.Should().Be(TagSet.EScope.Private);
    }

    [Fact]
    public void Unknown_top_level_properties_are_tolerated()
    {
        const string json = """
        { "public": { "game": ["ffxiv"] }, "private": {}, "future-field": 42 }
        """;
        var t = JsonSerializer.Deserialize<TagSet>(json);
        t.Should().NotBeNull();
        t!.Has("ffxiv").Should().BeTrue();
    }
}
