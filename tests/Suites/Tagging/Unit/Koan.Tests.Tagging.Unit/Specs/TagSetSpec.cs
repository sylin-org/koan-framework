using Koan.Tagging;

namespace Koan.Tests.Tagging.Unit.Specs;

public sealed class TagSetSpec
{
    [Fact]
    public void Public_and_private_are_independent()
    {
        var t = new TagSet();
        t.Public["game"].Set("ffxiv");
        t.Private["moderation"].Set("review-pending");

        t.Public.Contains("ffxiv").Should().BeTrue();
        t.Public.Contains("review-pending").Should().BeFalse();
        t.Private.Contains("review-pending").Should().BeTrue();
        t.Private.Contains("ffxiv").Should().BeFalse();
    }

    [Fact]
    public void Has_defaults_to_Public_scope()
    {
        var t = new TagSet();
        t.Public["game"].Set("ffxiv");
        t.Private["moderation"].Set("review-pending");

        t.Has("ffxiv").Should().BeTrue();
        t.Has("review-pending").Should().BeFalse();          // default Public, not found
    }

    [Fact]
    public void Has_with_explicit_scope_searches_correctly()
    {
        var t = new TagSet();
        t.Public["game"].Set("ffxiv");
        t.Private["moderation"].Set("review-pending");

        t.Has("ffxiv", TagSet.EScope.Public).Should().BeTrue();
        t.Has("ffxiv", TagSet.EScope.Private).Should().BeFalse();
        t.Has("review-pending", TagSet.EScope.Private).Should().BeTrue();
        t.Has("review-pending", TagSet.EScope.All).Should().BeTrue();
        t.Has("ffxiv", TagSet.EScope.All).Should().BeTrue();
        t.Has("nothing", TagSet.EScope.All).Should().BeFalse();
    }

    [Fact]
    public void Find_returns_location_for_public_tag()
    {
        var t = new TagSet();
        t.Public["game"].Set("ffxiv");

        var loc = t.Find("ffxiv");
        loc.Should().NotBeNull();
        loc!.Scope.Should().Be(TagSet.EScope.Public);
        loc.Category.Should().Be("game");
    }

    [Fact]
    public void Find_returns_location_for_private_tag_when_not_public()
    {
        var t = new TagSet();
        t.Private["moderation"].Set("review-pending");

        var loc = t.Find("review-pending");
        loc.Should().NotBeNull();
        loc!.Scope.Should().Be(TagSet.EScope.Private);
        loc.Category.Should().Be("moderation");
    }

    [Fact]
    public void Find_returns_null_for_unknown_tag()
    {
        var t = new TagSet();
        t.Find("nothing").Should().BeNull();
    }

    [Fact]
    public void Find_prefers_public_when_tag_appears_in_both()
    {
        var t = new TagSet();
        t.Public["game"].Set("ffxiv");
        t.Private["audit"].Set("ffxiv");

        var loc = t.Find("ffxiv");
        loc!.Scope.Should().Be(TagSet.EScope.Public);
        loc.Category.Should().Be("game");
    }

    [Fact]
    public void PublicTags_is_flat_dedup_of_public_only()
    {
        var t = new TagSet();
        t.Public["game"].Set("ffxiv");
        t.Public["technique"].Set(["dof", "clarity"]);
        t.Public["alt"].Set("ffxiv");                    // duplicate across categories
        t.Private["moderation"].Set("review-pending");   // not included in PublicTags

        t.PublicTags.Should().BeEquivalentTo(["ffxiv", "dof", "clarity"]);
        t.PublicTags.Should().NotContain("review-pending");
    }

    [Fact]
    public void PrivateTags_is_flat_dedup_of_private_only()
    {
        var t = new TagSet();
        t.Public["game"].Set("ffxiv");
        t.Private["moderation"].Set("review-pending");
        t.Private["audit"].Set("imported");

        t.PrivateTags.Should().BeEquivalentTo(["review-pending", "imported"]);
        t.PrivateTags.Should().NotContain("ffxiv");
    }

    [Fact]
    public void IsEmpty_reflects_both_scopes()
    {
        var t = new TagSet();
        t.IsEmpty.Should().BeTrue();

        t.Public["game"].Set("ffxiv");
        t.IsEmpty.Should().BeFalse();

        t.Public["game"].Clear();
        t.IsEmpty.Should().BeTrue();

        t.Private["audit"].Set("seen");
        t.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Example_from_design_conversation_reads_naturally()
    {
        var a = new TagSet();
        a.Private["category"].Set("a-private-tag").Unset("removed-private-tag");
        a.Public["import"].Set("a-public-import-tag");
        a.Public["source"].Set(["xivmods"]);
        a.Public["game"].Set(["ffxiv"]);

        a.PublicTags.Should().BeEquivalentTo(["a-public-import-tag", "xivmods", "ffxiv"]);
    }
}
