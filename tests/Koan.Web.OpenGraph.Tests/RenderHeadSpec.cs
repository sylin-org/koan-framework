using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.OpenGraph.Tests;

/// <summary>
/// WEB-0085: a server-rendered page hands its card to the renderer and places the head block in its own head. No shell is
/// configured in these hosts, and nothing the renderer does reads a route or an Entity.
/// </summary>
[Collection(AmbientHost.Name)]
public sealed class RenderHeadSpec
{
    private static Task<IntegrationHost> Host(Action? cards = null, params (string Key, string Value)[] settings)
    {
        var configure = KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .WithSetting("Koan:Web:OpenGraph:SiteName", "Configured Site")
            .ConfigureServices(s => s.AddKoan(cards ?? (() => { })));
        foreach (var (key, value) in settings) configure = configure.WithSetting(key, value);
        return configure.StartAsync();
    }

    private static string Render(IntegrationHost host, SocialCard card, string path = "/t/the-workshop/hello")
    {
        var context = new DefaultHttpContext { RequestServices = host.Services };
        context.Request.Method = "GET";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.org");
        context.Request.Path = path;
        return host.Services.GetRequiredService<IOpenGraphCardRenderer>().RenderHead(card, context.Request);
    }

    [Fact]
    public async Task A_card_renders_its_whole_head_with_no_shell_configured()
    {
        await using var host = await Host();

        var head = Render(host, new SocialCard("Hello", "First words.", CardImage.Raw("cover"), "/t/the-workshop/hello", "article", "The place"));

        head.Should().Contain("<title>Hello</title>");
        head.Should().Contain("<link rel=\"canonical\" href=\"https://example.org/t/the-workshop/hello\" />");
        head.Should().Contain("<meta property=\"og:title\" content=\"Hello\" />");
        head.Should().Contain("<meta property=\"og:description\" content=\"First words.\" />");
        head.Should().Contain("<meta property=\"og:image\" content=\"https://example.org/media/cover\" />");
        head.Should().Contain("<meta property=\"og:url\" content=\"https://example.org/t/the-workshop/hello\" />");
        head.Should().Contain("<meta property=\"og:type\" content=\"article\" />");
        head.Should().Contain("<meta property=\"og:site_name\" content=\"The place\" />");
        head.Should().Contain("<meta name=\"twitter:title\" content=\"Hello\" />");
        head.Should().NotContain("<html").And.NotContain("Configured Site");
    }

    [Fact]
    public async Task A_card_chooses_its_own_twitter_card()
    {
        await using var host = await Host(settings: ("Koan:Web:OpenGraph:TwitterCard", "summary"));

        var chosen = Render(host, new SocialCard("Hello", TwitterCard: "summary_large_image"));
        chosen.Should().Contain("<meta name=\"twitter:card\" content=\"summary_large_image\" />");
        chosen.Should().NotContain("<meta name=\"twitter:card\" content=\"summary\" />");

        Render(host, new SocialCard("Hello")).Should().Contain("<meta name=\"twitter:card\" content=\"summary\" />",
            "a card that says nothing takes the configured value");
    }

    [Fact]
    public async Task Values_are_encoded_and_truncated_as_a_declared_cards_are()
    {
        await using var host = await Host();

        var head = Render(host, new SocialCard("<script>alert(1)</script>", new string('d', 300)));

        head.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
        head.Should().Contain("&#x2026;").And.NotContain(new string('d', 200));
    }

    [Fact]
    public async Task An_absolute_url_stays_and_a_url_left_out_takes_the_requests_path()
    {
        await using var host = await Host();

        Render(host, new SocialCard("Hello", Url: "https://elsewhere.example/hello"))
            .Should().Contain("<meta property=\"og:url\" content=\"https://elsewhere.example/hello\" />");
        Render(host, new SocialCard("Hello"), "/t/the-workshop")
            .Should().Contain("<meta property=\"og:url\" content=\"https://example.org/t/the-workshop\" />");
    }

    [Fact]
    public async Task Values_left_out_take_the_configured_defaults_and_a_handed_site_name_titles_a_card_with_none()
    {
        await using var host = await Host(null,
            ("Koan:Web:OpenGraph:DefaultDescription", "About the site"),
            ("Koan:Web:OpenGraph:DefaultImage", "/img/default.png"));

        var defaults = Render(host, new SocialCard());
        defaults.Should().Contain("<meta property=\"og:title\" content=\"Configured Site\" />");
        defaults.Should().Contain("<meta property=\"og:site_name\" content=\"Configured Site\" />");
        defaults.Should().Contain("<meta property=\"og:description\" content=\"About the site\" />");
        defaults.Should().Contain("<meta property=\"og:image\" content=\"https://example.org/img/default.png\" />");
        defaults.Should().Contain("<meta property=\"og:type\" content=\"website\" />");

        var named = Render(host, new SocialCard(SiteName: "The place"));
        named.Should().Contain("<meta property=\"og:title\" content=\"The place\" />");
        named.Should().Contain("<meta property=\"og:site_name\" content=\"The place\" />");
    }

    [Fact]
    public async Task The_head_toggles_hold_and_a_disabled_pillar_renders_nothing()
    {
        await using (var toggled = await Host(null,
            ("Koan:Web:OpenGraph:EmitTitleElement", "false"),
            ("Koan:Web:OpenGraph:EmitCanonical", "false"),
            ("Koan:Web:OpenGraph:EmitTwitterTags", "false")))
        {
            var head = Render(toggled, new SocialCard("Hello"));
            head.Should().NotContain("<title>").And.NotContain("canonical").And.NotContain("twitter:");
            head.Should().Contain("<meta property=\"og:title\" content=\"Hello\" />");
        }

        await using var disabled = await Host(null, ("Koan:Web:OpenGraph:Enabled", "false"));
        Render(disabled, new SocialCard("Hello")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_path_a_declared_card_routes_runs_no_resolver()
    {
        var resolved = 0;
        await using var host = await Host(() => SocialCards.For<TestWork>("/work/{id}", id =>
        {
            Interlocked.Increment(ref resolved);
            return TestWork.Get(id);
        }).Title(work => work.Name));
        var previous = AppHost.Current;
        AppHost.Current = host.Services;
        try
        {
            var work = await new TestWork { Name = "The declared card" }.Save();

            var head = Render(host, new SocialCard("The handed card"), $"/work/{work.Id}");

            head.Should().Contain("The handed card").And.NotContain("The declared card");
            resolved.Should().Be(0);
        }
        finally
        {
            AppHost.Current = previous!;
        }
    }
}
