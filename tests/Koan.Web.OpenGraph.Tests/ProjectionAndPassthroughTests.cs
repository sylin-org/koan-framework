using System.IO;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Koan.Web.OpenGraph.Tests;

/// <summary>
/// Unit coverage for the parts that need no data layer: the projection-to-snapshot mapping, the
/// image path composition, and the renderer's two passthrough exits (disabled, shell unavailable).
/// </summary>
public sealed class ProjectionAndPassthroughTests
{
    public ProjectionAndPassthroughTests() => SocialCards.Reset();

    [Fact]
    public void CardImage_recipe_composes_a_media_path()
    {
        CardImage.Recipe("share-card", "abc").ToPath().Should().Be("/media/abc/share-card");
        CardImage.Raw("abc").ToPath().Should().Be("/media/abc");
        CardImage.Url("https://cdn/x.png").ToPath().Should().Be("https://cdn/x.png");
    }

    [Fact]
    public void CardImage_empty_media_collapses_to_default()
    {
        CardImage.Recipe("share-card", null).ToPath().Should().BeNull();
        CardImage.Raw("").ToPath().Should().BeNull();
        CardImage.Default.ToPath().Should().BeNull();
    }

    [Fact]
    public void FromCard_hard_caps_stored_text()
    {
        var longTitle = new string('t', SocialCardSnapshot.MaxStoredTitleLength + 50);
        var card = new SocialCard(longTitle, "desc", CardImage.Recipe("share-card", "abc"), "/work/1", "article");

        var snapshot = SocialCardSnapshot.FromCard("TestWork:1", card);

        snapshot.Title!.Length.Should().Be(SocialCardSnapshot.MaxStoredTitleLength);
        snapshot.ImagePath.Should().Be("/media/abc/share-card");
        snapshot.UrlPath.Should().Be("/work/1");
        snapshot.OgType.Should().Be("article");
    }

    [Fact]
    public async Task Disabled_options_pass_through_as_null()
    {
        var options = new OpenGraphOptions { Enabled = false, ShellPath = "anything" };
        var renderer = new OpenGraphCardRenderer(new TestOptionsMonitor<OpenGraphOptions>(options), new ShellCache());

        var result = await renderer.RenderShellAsync(NavRequest("/work/abc"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Missing_shell_passes_through_as_null()
    {
        var options = new OpenGraphOptions { Enabled = true, ShellPath = Path.Combine(Path.GetTempPath(), "does-not-exist-" + System.Guid.NewGuid().ToString("N") + ".html") };
        var renderer = new OpenGraphCardRenderer(new TestOptionsMonitor<OpenGraphOptions>(options), new ShellCache());

        var result = await renderer.RenderShellAsync(NavRequest("/work/abc"));

        result.Should().BeNull();
    }

    private static HttpRequest NavRequest(string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("example.org");
        ctx.Request.Path = path;
        ctx.Request.Headers.Accept = "text/html";
        return ctx.Request;
    }
}
