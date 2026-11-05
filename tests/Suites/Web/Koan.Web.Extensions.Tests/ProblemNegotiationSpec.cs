using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>ARCH-0143: a refusal is one problem document whatever a client asks for, a browser included; a page is the application's to render.</summary>
[Collection(RestEntityCollection.Name)]
public sealed class ProblemNegotiationSpec(RestEntityWebFactory fx)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("*/*")]
    [InlineData("text/html")]
    [InlineData("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8")]
    [InlineData("application/json")]
    [InlineData("application/problem+json")]
    public async Task Every_client_reads_one_problem(string? accept)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tool-route/gated/refused");
        if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
        using var response = await fx.Client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("kind").GetString().Should().Be("gated");
        body.RootElement.GetProperty("word").GetString().Should().Be("timed-out");
        body.RootElement.GetProperty("title").GetString().Should().Be("Gated", "a title names the kind");
    }
}
