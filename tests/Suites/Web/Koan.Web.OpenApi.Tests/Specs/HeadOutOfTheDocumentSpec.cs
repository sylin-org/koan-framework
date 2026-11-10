using System.Text.Json.Nodes;
using AwesomeAssertions;
using Xunit;

namespace Koan.Web.OpenApi.Tests;

/// <summary>WEB-0091: a read answers <c>HEAD</c> wherever it answers <c>GET</c>, and the document lists it once, as its <c>GET</c>.</summary>
public sealed class HeadOutOfTheDocumentSpec(OpenApiWebApplicationFactory factory) : IClassFixture<OpenApiWebApplicationFactory>
{
    [Fact]
    public async Task A_read_is_listed_once_as_its_get()
    {
        using var client = factory.CreateClient();

        var paths = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!["paths"]!.AsObject();
        var operations = paths.SelectMany(path => path.Value!.AsObject().Select(operation => operation.Key)).ToArray();

        operations.Should().Contain("get");
        operations.Should().NotContain("head");
    }
}
