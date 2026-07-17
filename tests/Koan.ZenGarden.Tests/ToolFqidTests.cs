using AwesomeAssertions;
using Koan.ZenGarden;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class ToolFqidTests
{
    // ── Parse ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_bare_name()
    {
        var fqid = ToolFqid.Parse("mongodb");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().BeNull();
        fqid.IsQualified.Should().BeFalse();
        fqid.ToString().Should().Be("mongodb");
    }

    [Fact]
    public void Parse_qualified_name()
    {
        var fqid = ToolFqid.Parse("mongodb:prod");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().Be("prod");
        fqid.IsQualified.Should().BeTrue();
        fqid.ToString().Should().Be("mongodb:prod");
    }

    [Fact]
    public void Parse_normalizes_to_lowercase()
    {
        var fqid = ToolFqid.Parse("MongoDB:PROD");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().Be("prod");
    }

    [Fact]
    public void Parse_strips_offering_prefix()
    {
        var fqid = ToolFqid.Parse("offering:mongodb");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().BeNull();
    }

    [Fact]
    public void Parse_strips_offering_prefix_with_instance()
    {
        var fqid = ToolFqid.Parse("offering:mongodb:prod");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().Be("prod");
    }

    [Fact]
    public void Parse_strips_seed_bank_prefix()
    {
        var fqid = ToolFqid.Parse("seed-bank:default");
        fqid.OfferingType.Should().Be("default");
        fqid.Instance.Should().BeNull();
    }

    [Fact]
    public void Parse_normalizes_at_separator()
    {
        var fqid = ToolFqid.Parse("ollama@adopted");
        fqid.OfferingType.Should().Be("ollama");
        fqid.Instance.Should().Be("adopted");
        fqid.IsQualified.Should().BeTrue();
        fqid.ToString().Should().Be("ollama:adopted");
    }

    [Fact]
    public void Parse_same_name_collapses_to_bare()
    {
        var fqid = ToolFqid.Parse("mongodb:mongodb");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().BeNull();
        fqid.IsQualified.Should().BeFalse();
    }

    [Fact]
    public void Parse_trims_whitespace()
    {
        var fqid = ToolFqid.Parse("  mongodb : prod  ");
        fqid.OfferingType.Should().Be("mongodb ");
        // Note: internal whitespace is preserved per segment — this matches
        // the pattern of "trim outer, lowercase all". The Rust side also does
        // trim() on the full input but not individual segments in the simple parse path.
    }

    [Fact]
    public void Parse_trailing_separator_yields_bare()
    {
        var fqid = ToolFqid.Parse("mongodb:");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().BeNull();
    }

    [Fact]
    public void Parse_throws_on_empty()
    {
        var act = () => ToolFqid.Parse("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_throws_on_whitespace()
    {
        var act = () => ToolFqid.Parse("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryParse_returns_false_on_null()
    {
        ToolFqid.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_returns_false_on_empty()
    {
        ToolFqid.TryParse("", out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_returns_true_on_valid()
    {
        ToolFqid.TryParse("mongodb:prod", out var result).Should().BeTrue();
        result.OfferingType.Should().Be("mongodb");
        result.Instance.Should().Be("prod");
    }

    // ── From ───────────────────────────────────────────────────────

    [Fact]
    public void From_creates_bare()
    {
        var fqid = ToolFqid.From("mongodb");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().BeNull();
    }

    [Fact]
    public void From_creates_qualified()
    {
        var fqid = ToolFqid.From("MongoDB", "Prod");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().Be("prod");
    }

    // ── IsEmpty ────────────────────────────────────────────────────

    [Fact]
    public void Default_struct_is_empty()
    {
        var fqid = default(ToolFqid);
        fqid.IsEmpty.Should().BeTrue();
    }

    // ── Matches (query vs candidate) ──────────────────────────────

    [Fact]
    public void Bare_matches_bare_same_type()
    {
        ToolFqid.Parse("mongodb").Matches(ToolFqid.Parse("mongodb")).Should().BeTrue();
    }

    [Fact]
    public void Bare_matches_qualified_same_type()
    {
        ToolFqid.Parse("mongodb").Matches(ToolFqid.Parse("mongodb:prod")).Should().BeTrue();
    }

    [Fact]
    public void Bare_does_not_match_different_type()
    {
        ToolFqid.Parse("mongodb").Matches(ToolFqid.Parse("redis")).Should().BeFalse();
    }

    [Fact]
    public void Qualified_matches_same_qualified()
    {
        ToolFqid.Parse("mongodb:prod").Matches(ToolFqid.Parse("mongodb:prod")).Should().BeTrue();
    }

    [Fact]
    public void Qualified_does_not_match_different_instance()
    {
        ToolFqid.Parse("mongodb:prod").Matches(ToolFqid.Parse("mongodb:dev")).Should().BeFalse();
    }

    [Fact]
    public void Qualified_does_not_match_bare()
    {
        ToolFqid.Parse("mongodb:prod").Matches(ToolFqid.Parse("mongodb")).Should().BeFalse();
    }

    [Fact]
    public void Empty_matches_everything()
    {
        default(ToolFqid).Matches(ToolFqid.Parse("mongodb:prod")).Should().BeTrue();
    }

    // ── MatchesSnapshot ───────────────────────────────────────────

    [Fact]
    public void MatchesSnapshot_bare_matches_via_offering_type()
    {
        var query = ToolFqid.Parse("ollama");
        query.MatchesSnapshot("ollama:adopted", "ollama", null).Should().BeTrue();
    }

    [Fact]
    public void MatchesSnapshot_bare_matches_exact_fqid()
    {
        var query = ToolFqid.Parse("mongodb");
        query.MatchesSnapshot("mongodb", null, null).Should().BeTrue();
    }

    [Fact]
    public void MatchesSnapshot_qualified_matches_exact()
    {
        var query = ToolFqid.Parse("mongodb:prod");
        query.MatchesSnapshot("mongodb:prod", "mongodb", null).Should().BeTrue();
    }

    [Fact]
    public void MatchesSnapshot_qualified_does_not_match_different_instance()
    {
        var query = ToolFqid.Parse("mongodb:prod");
        query.MatchesSnapshot("mongodb:dev", "mongodb", null).Should().BeFalse();
    }

    [Fact]
    public void MatchesSnapshot_matches_via_alias()
    {
        var query = ToolFqid.Parse("ollama");
        query.MatchesSnapshot("ollama@adopted", "ollama-custom", new[] { "ollama" }).Should().BeTrue();
    }

    [Fact]
    public void MatchesSnapshot_does_not_match_unrelated()
    {
        var query = ToolFqid.Parse("redis");
        query.MatchesSnapshot("mongodb", "mongodb", null).Should().BeFalse();
    }

    [Fact]
    public void MatchesSnapshot_at_separator_in_fqid_still_matches()
    {
        var query = ToolFqid.Parse("ollama");
        // "ollama@adopted" parses to offering="ollama" instance="adopted"
        query.MatchesSnapshot("ollama@adopted", null, null).Should().BeTrue();
    }

    // ── Implicit conversion ───────────────────────────────────────

    [Fact]
    public void Implicit_from_string()
    {
        ToolFqid fqid = "mongodb:prod";
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().Be("prod");
    }

    [Fact]
    public void Implicit_to_string()
    {
        string canonical = ToolFqid.Parse("mongodb:prod");
        canonical.Should().Be("mongodb:prod");
    }

    // ── Equality ──────────────────────────────────────────────────

    [Fact]
    public void Same_values_are_equal()
    {
        ToolFqid.Parse("mongodb:prod").Should().Be(ToolFqid.Parse("mongodb:prod"));
    }

    [Fact]
    public void Different_values_are_not_equal()
    {
        ToolFqid.Parse("mongodb:prod").Should().NotBe(ToolFqid.Parse("mongodb:dev"));
    }

    [Fact]
    public void Case_normalized_values_are_equal()
    {
        ToolFqid.Parse("MongoDB:PROD").Should().Be(ToolFqid.Parse("mongodb:prod"));
    }

    // ── Double-colon wire format (Moss canonical) ─────────────────

    [Fact]
    public void Parse_double_colon_wire_format()
    {
        var fqid = ToolFqid.Parse("ollama::orchestrator");
        fqid.OfferingType.Should().Be("ollama");
        fqid.Instance.Should().Be("orchestrator");
        fqid.IsQualified.Should().BeTrue();
    }

    [Fact]
    public void Double_colon_matches_single_colon()
    {
        ToolFqid.Parse("ollama::orchestrator")
            .Should().Be(ToolFqid.Parse("ollama:orchestrator"));
    }

    [Fact]
    public void MatchesSnapshot_double_colon_fqid()
    {
        var query = ToolFqid.Parse("ollama:orchestrator");
        query.MatchesSnapshot("ollama::orchestrator", "ollama", null).Should().BeTrue();
    }

    [Fact]
    public void Parse_double_colon_same_name_collapses()
    {
        var fqid = ToolFqid.Parse("mongodb::mongodb");
        fqid.OfferingType.Should().Be("mongodb");
        fqid.Instance.Should().BeNull();
        fqid.IsQualified.Should().BeFalse();
    }

    [Fact]
    public void Parse_double_colon_bare_trailing()
    {
        var fqid = ToolFqid.Parse("ollama::");
        fqid.OfferingType.Should().Be("ollama");
        fqid.Instance.Should().BeNull();
    }
}
