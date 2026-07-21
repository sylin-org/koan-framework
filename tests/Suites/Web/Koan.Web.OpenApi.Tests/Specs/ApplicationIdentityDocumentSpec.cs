using System.Text.Json.Nodes;
using AwesomeAssertions;
using Xunit;

namespace Koan.Web.OpenApi.Tests;

public sealed class ApplicationIdentityDocumentSpec : IClassFixture<OpenApiWebApplicationFactory>
{
    private readonly OpenApiWebApplicationFactory _factory;

    public ApplicationIdentityDocumentSpec(OpenApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Parallel_hosts_render_their_own_application_identity()
    {
        using var clientA = _factory.CreateClient(
            ("Koan:Application:Name", "Application Alpha"),
            ("Koan:Application:Code", "application-alpha"));
        using var clientB = _factory.CreateClient(
            ("Koan:Application:Name", "Application Beta"),
            ("Koan:Application:Code", "application-beta"));

        var documentA = await ReadDocument(clientA);
        var documentB = await ReadDocument(clientB);

        documentA["info"]?["title"]?.GetValue<string>().Should().Be("Application Alpha");
        documentA["x-koan-application-code"]?.GetValue<string>().Should().Be("application-alpha");
        documentB["info"]?["title"]?.GetValue<string>().Should().Be("Application Beta");
        documentB["x-koan-application-code"]?.GetValue<string>().Should().Be("application-beta");
    }

    private static async Task<JsonObject> ReadDocument(HttpClient client)
    {
        var json = await client.GetStringAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        return JsonNode.Parse(json)!.AsObject();
    }
}
