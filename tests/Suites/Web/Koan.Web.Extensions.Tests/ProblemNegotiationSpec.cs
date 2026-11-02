using System.Net;
using System.Text.Json;
using AwesomeAssertions;
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
}
