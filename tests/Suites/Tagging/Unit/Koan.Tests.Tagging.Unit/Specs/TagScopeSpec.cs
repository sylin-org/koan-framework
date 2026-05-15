using Koan.Tagging;

namespace Koan.Tests.Tagging.Unit.Specs;

public sealed class TagScopeSpec
{
    [Fact]
    public void Indexer_auto_creates_missing_categories()
    {
        var scope = new TagScope();
        scope["new-category"].Set("x");
        scope.Categories.Should().ContainKey("new-category");
        scope.Categories["new-category"].Should().ContainSingle().Which.Should().Be("x");
    }

    [Fact]
    public void Indexer_is_case_insensitive_for_category_names()
    {
        var scope = new TagScope();
        scope["Game"].Set("ffxiv");
        scope["game"].Set("sims4");
        scope.Categories.Should().ContainKey("Game");        // preserves first-write casing
        scope.Categories["GAME"].Should().HaveCount(2);     // case-insensitive lookup
    }

    [Fact]
    public void Contains_searches_across_all_categories()
    {
        var scope = new TagScope();
        scope["game"].Set("ffxiv");
        scope["technique"].Set("dof");
        scope.Contains("ffxiv").Should().BeTrue();
        scope.Contains("dof").Should().BeTrue();
        scope.Contains("nothing").Should().BeFalse();
    }

    [Fact]
    public void Locate_returns_category_name_for_a_present_tag()
    {
        var scope = new TagScope();
        scope["game"].Set("ffxiv");
        scope["technique"].Set("dof");
        scope.Locate("ffxiv").Should().Be("game");
        scope.Locate("dof").Should().Be("technique");
        scope.Locate("nothing").Should().BeNull();
    }

    [Fact]
    public void Flat_returns_deduplicated_union()
    {
        var scope = new TagScope();
        scope["game"].Set(["ffxiv", "sims4"]);
        scope["alt"].Set("ffxiv");        // duplicate across categories
        scope.Flat.Should().HaveCount(2);
        scope.Flat.Should().BeEquivalentTo(["ffxiv", "sims4"]);
    }

    [Fact]
    public void IsEmpty_is_true_when_all_categories_are_empty()
    {
        var scope = new TagScope();
        scope.IsEmpty.Should().BeTrue();

        _ = scope["game"];               // auto-create empty
        scope.IsEmpty.Should().BeTrue(); // still empty (auto-created category has zero tags)

        scope["game"].Set("ffxiv");
        scope.IsEmpty.Should().BeFalse();
    }
}
