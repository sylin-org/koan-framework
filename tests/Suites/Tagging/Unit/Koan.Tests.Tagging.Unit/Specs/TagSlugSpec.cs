using Koan.Tagging;

namespace Koan.Tests.Tagging.Unit.Specs;

public sealed class TagSlugSpec
{
    [Theory]
    [InlineData("Au Ra", "au-ra")]
    [InlineData("Miqo'te", "miqote")]                     // apostrophe stripped, no hyphen
    [InlineData("Au'Ra", "aura")]                          // both halves run together after strip
    [InlineData("Seeker of the Sun", "seeker-of-the-sun")]
    [InlineData("Keeper of the Moon", "keeper-of-the-moon")]
    [InlineData("Final Fantasy XIV", "final-fantasy-xiv")]
    public void Converts_canonical_FFXIV_names(string raw, string expected)
        => TagSlug.From(raw).Should().Be(expected);

    [Theory]
    [InlineData("HYUR", "hyur")]
    [InlineData("hYuR", "hyur")]
    public void Lowercases_input(string raw, string expected)
        => TagSlug.From(raw).Should().Be(expected);

    [Theory]
    [InlineData("__leading", "leading")]
    [InlineData("trailing__", "trailing")]
    [InlineData("a   b", "a-b")]                           // collapse runs of non-alnum
    [InlineData("a---b", "a-b")]
    [InlineData(" a b c ", "a-b-c")]
    public void Trims_and_collapses(string raw, string expected)
        => TagSlug.From(raw).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Empty_or_whitespace_returns_empty(string? raw)
        => TagSlug.From(raw).Should().Be(string.Empty);

    [Theory]
    [InlineData("Hyur", "hyur")]
    [InlineData("Au Ra", "au-ra")]
    public void Is_idempotent_against_already_slugged_input(string raw, string slug)
    {
        TagSlug.From(slug).Should().Be(slug);
        TagSlug.From(raw).Should().Be(slug);
    }
}
