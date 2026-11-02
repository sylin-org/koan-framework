using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>ARCH-0142: a page whose API is its route answers a browser with a page and a client with a problem, from one mapping.</summary>
[Collection(RestEntityCollection.Name)]
public sealed class ProblemNegotiationSpec(RestEntityWebFactory fx)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string BrowserAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8";

    private static HttpRequestMessage Request(string path, string? accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
        return request;
    }

    [Fact]
    public async Task A_browser_reads_a_page_with_the_same_status_and_a_next_step()
    {
        using var response = await fx.Client.SendAsync(Request("/tool-route/gated/refused", BrowserAccept), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var page = await response.Content.ReadAsStringAsync(Ct);
        page.Should().Contain("Not for you.").And.Contain("href=\"/appeal\"").And.NotContain("\"kind\"");
    }

    [Fact]
    public async Task A_page_says_what_an_application_wrote_as_text()
    {
        using var response = await fx.Client.SendAsync(Request("/tool-route/script/refused", "text/html"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var page = await response.Content.ReadAsStringAsync(Ct);
        page.Should().Contain("&lt;script&gt;").And.NotContain("<script>");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*/*")]
    [InlineData("application/json")]
    [InlineData("application/problem+json")]
    [InlineData("application/json, text/html;q=0.5")]
    public async Task Everything_else_reads_a_problem(string? accept)
    {
        using var response = await fx.Client.SendAsync(Request("/tool-route/gated/refused", accept), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("gate").GetString().Should().Be("timed-out");
        body.RootElement.GetProperty("title").GetString().Should().Be("Timed out", "a gate's heading is the problem's title");
    }

    [Fact]
    public async Task An_applications_view_answers_in_place_of_the_plain_page()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment("Development");
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Development",
                    ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                }));
                web.ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddKoanControllersFrom<CogController>();
                    services.AddSingleton<IProblemView, OwnView>();
                });
                web.Configure(_ => { });
            })
            .StartAsync(Ct);
        using var client = host.GetTestClient();

        using var page = await client.SendAsync(Request("/tool-route/conflict/refused", BrowserAccept), Ct);
        page.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await page.Content.ReadAsStringAsync(Ct)).Should().Be("own: conflict: Already there.");

        using var json = await client.SendAsync(Request("/tool-route/conflict/refused", "application/json"), Ct);
        json.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        await host.StopAsync(Ct);
    }

    private sealed class OwnView : IProblemView
    {
        public IActionResult Render(HttpContext http, ProblemDetails problem) => new ContentResult
        {
            StatusCode = problem.Status,
            ContentType = "text/html",
            Content = $"own: {problem.Extensions["kind"]}: {problem.Detail}",
        };
    }
}
