using AwesomeAssertions;
using Xunit;

namespace Koan.Web.OpenGraph.Tests;

public sealed class RouteTokenMatcherTests
{
    [Fact]
    public void Matches_simple_route_and_extracts_token()
    {
        var matcher = new RouteTokenMatcher("/work/{id}");

        matcher.TryExtractToken("/work/019ebf08636e", out var token).Should().BeTrue();
        token.Should().Be("019ebf08636e");
    }

    [Fact]
    public void Discards_trailing_slug_segment()
    {
        var matcher = new RouteTokenMatcher("/work/{id}");

        matcher.TryExtractToken("/work/019ebf08636e/prkrmancer-reshade-preset", out var token).Should().BeTrue();
        token.Should().Be("019ebf08636e");
    }

    [Fact]
    public void Matches_with_trailing_slash()
    {
        var matcher = new RouteTokenMatcher("/work/{id}");

        matcher.TryExtractToken("/work/019ebf08636e/", out var token).Should().BeTrue();
        token.Should().Be("019ebf08636e");
    }

    [Fact]
    public void Does_not_match_a_different_prefix()
    {
        var matcher = new RouteTokenMatcher("/work/{id}");

        matcher.TryExtractToken("/articles/hello-world", out _).Should().BeFalse();
    }

    [Fact]
    public void Does_not_match_when_token_segment_is_absent()
    {
        var matcher = new RouteTokenMatcher("/work/{id}");

        matcher.TryExtractToken("/work", out _).Should().BeFalse();
    }

    [Fact]
    public void Slug_route_uses_the_slug_as_the_token()
    {
        var matcher = new RouteTokenMatcher("/articles/{slug}");

        matcher.TryExtractToken("/articles/shaders-101-getting-started", out var token).Should().BeTrue();
        token.Should().Be("shaders-101-getting-started");
    }

    [Fact]
    public void Explicit_catch_all_template_extracts_the_primary_token()
    {
        var matcher = new RouteTokenMatcher("/work/{id}/{**slug}");

        matcher.TryExtractToken("/work/abc/x/y/z", out var token).Should().BeTrue();
        token.Should().Be("abc");
    }

    [Fact]
    public void Explicit_slug_segment_uses_the_first_token_and_discards_the_slug()
    {
        var matcher = new RouteTokenMatcher("/work/{id}/{slug}");

        matcher.TryExtractToken("/work/abc/my-seo-slug", out var withSlug).Should().BeTrue();
        withSlug.Should().Be("abc");

        // The declared slug is decorative; a request without it still resolves off the id.
        matcher.TryExtractToken("/work/abc", out var withoutSlug).Should().BeTrue();
        withoutSlug.Should().Be("abc");
    }

    [Fact]
    public void Rejects_a_template_with_no_token()
    {
        var act = () => new RouteTokenMatcher("/static/page");

        act.Should().Throw<ArgumentException>();
    }
}
