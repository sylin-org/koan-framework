using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Xunit;

namespace S4.Web.IntegrationTests;

public sealed class S4GraphQlTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static bool ShouldRun() => string.Equals(Environment.GetEnvironmentVariable("SORA_ENABLE_S4_TESTS"), "1");

    public S4GraphQlTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("urls", "http://localhost:0");
            builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
        });
    }

    private static StringContent Gql(string query, object? variables = null)
    {
    var payload = new { query, variables };
    var json = JsonConvert.SerializeObject(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    [Fact]
    public async Task items_query_and_upsert_should_work()
    {
        if (!ShouldRun()) return; // gated in local/dev to avoid external deps
        Sora.Data.Core.TestHooks.ResetDataConfigs();
        var client = _factory.CreateClient();

        // clear existing via REST helper
        await client.DeleteAsync("/api/items/clear");

        // upsert via GraphQL
        var m = "mutation($input: ItemInput!){ upsertItem(input:$input){ id name display } }";
        var mBody = Gql(m, new { input = new { name = "alpha" } });
        var mResp = await client.PostAsync("/graphql", mBody);
        mResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var mDoc = JToken.Parse(await mResp.Content.ReadAsStringAsync());
    mDoc["errors"].Should().BeNull();

        // list via GraphQL
        var q = "query($page:Int,$size:Int){ items(page:$page,size:$size){ totalCount items{ id name display } } }";
        var qBody = Gql(q, new { page = 1, size = 10 });
        var qResp = await client.PostAsync("/graphql", qBody);
        qResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var qDoc = JToken.Parse(await qResp.Content.ReadAsStringAsync());
    qDoc["errors"].Should().BeNull();
    var total = (int)qDoc["data"]!["items"]!["totalCount"]!;
        total.Should().BeGreaterOrEqualTo(1);
    var arr = (JArray)qDoc["data"]!["items"]!["items"]!;
    arr.Count.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task filter_query_should_return_subset()
    {
        if (!ShouldRun()) return; // gated in local/dev to avoid external deps
        Sora.Data.Core.TestHooks.ResetDataConfigs();
        var client = _factory.CreateClient();
        await client.DeleteAsync("/api/items/clear");
        // seed 3 items via REST helper
        await client.PostAsync("/api/items/seed/3", content: null);

        // filter by name contains '1'
        var q = "query($filter:String){ items(filter:$filter,page:1,size:50){ totalCount items{ id name display } } }";
        var filter = "{ \"Name\": { \"contains\": \"1\" } }";
        var body = Gql(q, new { filter });
        var resp = await client.PostAsync("/graphql", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var doc = JToken.Parse(await resp.Content.ReadAsStringAsync());
    doc["errors"].Should().BeNull();
    var data = doc["data"]!["items"]!;
    ((int)data["totalCount"]!).Should().BeGreaterOrEqualTo(1);
    var arr = (JArray)data["items"]!;
    arr.All(e => e!["name"]!.Value<string>()!.Contains("1")).Should().BeTrue();
    }
}
