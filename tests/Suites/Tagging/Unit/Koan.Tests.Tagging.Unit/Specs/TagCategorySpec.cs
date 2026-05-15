using Koan.Tagging;

namespace Koan.Tests.Tagging.Unit.Specs;

public sealed class TagCategorySpec
{
    [Fact]
    public void Set_adds_a_tag()
    {
        var cat = new TagCategory();
        cat.Set("ffxiv");
        cat.Contains("ffxiv").Should().BeTrue();
        cat.Count.Should().Be(1);
    }

    [Fact]
    public void Set_is_case_insensitive_for_dedup()
    {
        var cat = new TagCategory().Set("ffxiv").Set("FFXIV").Set("FfXiV");
        cat.Count.Should().Be(1);
        cat.Contains("ffxiv").Should().BeTrue();
        cat.Contains("FFXIV").Should().BeTrue();
    }

    [Fact]
    public void Set_trims_whitespace()
    {
        var cat = new TagCategory().Set("  ffxiv  ");
        cat.Should().ContainSingle().Which.Should().Be("ffxiv");
    }

    [Fact]
    public void Set_ignores_null_or_whitespace_input()
    {
        var cat = new TagCategory().Set("").Set("   ").Set((string)null!);
        cat.Count.Should().Be(0);
    }

    [Fact]
    public void Set_collection_overload_adds_each()
    {
        var cat = new TagCategory().Set(["ffxiv", "sims4", "cyberpunk-2077"]);
        cat.Count.Should().Be(3);
        cat.Should().BeEquivalentTo(["ffxiv", "sims4", "cyberpunk-2077"]);
    }

    [Fact]
    public void Unset_removes_an_existing_tag()
    {
        var cat = new TagCategory().Set("a").Set("b").Unset("a");
        cat.Contains("a").Should().BeFalse();
        cat.Contains("b").Should().BeTrue();
    }

    [Fact]
    public void Unset_missing_tag_is_noop()
    {
        var cat = new TagCategory().Set("a");
        cat.Unset("never-was");
        cat.Count.Should().Be(1);
    }

    [Fact]
    public void Toggle_adds_then_removes()
    {
        var cat = new TagCategory();
        cat.Toggle("ffxiv");
        cat.Contains("ffxiv").Should().BeTrue();
        cat.Toggle("ffxiv");
        cat.Contains("ffxiv").Should().BeFalse();
    }

    [Fact]
    public void Replace_clears_then_sets()
    {
        var cat = new TagCategory().Set("a").Set("b").Replace(["c", "d"]);
        cat.Should().BeEquivalentTo(["c", "d"]);
    }

    [Fact]
    public void Clear_empties_the_category()
    {
        var cat = new TagCategory().Set("a").Set("b").Clear();
        cat.Count.Should().Be(0);
    }

    [Fact]
    public void Fluent_API_chains_cleanly()
    {
        var cat = new TagCategory()
            .Set("a")
            .Set(["b", "c"])
            .Unset("b")
            .Toggle("d");

        cat.Should().BeEquivalentTo(["a", "c", "d"]);
    }
}
