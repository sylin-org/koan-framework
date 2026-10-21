using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>
/// SEC-0018: the HTTP self-description lists only what its caller may call. An anonymous caller's capability document
/// and SDK definitions omit an entity every verb of which needs authentication.
/// </summary>
public sealed class SelfDescriptionGatingSpec : IClassFixture<StreamableHttpFixture>
{
    private readonly StreamableHttpFixture _fx;

    public SelfDescriptionGatingSpec(StreamableHttpFixture fx) => _fx = fx;

    private static CancellationToken Quick => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    [Fact]
    public async Task The_capability_document_lists_only_callable_tools()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync("/mcp/capabilities", Quick);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var names = ((JArray)JObject.Parse(await res.Content.ReadAsStringAsync(Quick))["tools"]!)
            .Select(t => t["name"]!.Value<string>()).ToArray();
        names.Should().Contain("gizmo.collection");
        names.Should().NotContain(n => n!.StartsWith("strongroom."), "an anonymous caller can call no strongroom verb");
    }

    [Fact]
    public async Task The_sdk_definitions_declare_only_reachable_entities()
    {
        using var client = _fx.NewClient();
        var res = await client.GetAsync("/mcp/sdk/definitions", Quick);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync(Quick));

        var definitions = await res.Content.ReadAsStringAsync(Quick);
        definitions.Should().Contain("interface gizmo");
        definitions.Should().NotContain("strongroom");
    }
}
