using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Explorer.IntegrationTests;

/// <summary>SEC-0014: under <c>Exposure = Code</c> the console neither lists entity tools nor runs them.</summary>
public sealed class ExplorerExposureSpec : IClassFixture<CodeExposureExplorerFixture>, IDisposable
{
    private readonly CodeExposureExplorerFixture _fx;
    private readonly HttpClient _client;

    public ExplorerExposureSpec(CodeExposureExplorerFixture fx)
    {
        _fx = fx;
        _client = fx.NewClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Try_it_refuses_an_entity_tool()
    {
        var registry = _fx.Services.GetRequiredService<McpEntityRegistry>();
        var name = registry.Registrations.First(r => r.DisplayName == "trinket").Tools
            .First(t => t.Operation == EntityEndpointOperationKind.Collection).Name;
        var req = new HttpRequestMessage(HttpMethod.Post, "/mcp/explorer/call")
        {
            Content = new StringContent(new JObject { ["name"] = name, ["arguments"] = new JObject() }.ToString(), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation(TestAuthHandler.HeaderName, "role=user");

        var res = await _client.SendAsync(req, Ct);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden, await res.Content.ReadAsStringAsync(Ct));
        JObject.Parse(await res.Content.ReadAsStringAsync(Ct))["error"]!.Value<string>().Should().Be("exposure_mode");
    }

    [Fact]
    public async Task The_map_lists_no_entity_tools()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/map.json");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        var res = await _client.SendAsync(req, Ct);
        res.EnsureSuccessStatusCode();

        var map = JObject.Parse(await res.Content.ReadAsStringAsync(Ct));
        ((JArray)map["entities"]!).Should().BeEmpty();
        ((JArray)map["customTools"]!).Should().BeEmpty();
    }
}
