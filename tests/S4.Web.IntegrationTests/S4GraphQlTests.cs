using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace S4.Web.IntegrationTests;

public sealed class S4GraphQlTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

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
        var json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    [Fact]
    public async Task items_query_and_upsert_should_work()
    {
        Sora.Data.Core.TestHooks.ResetDataConfigs();
        var client = _factory.CreateClient();

        // clear existing via REST helper
        await client.DeleteAsync("/api/items/clear");

        // upsert via GraphQL
        var m = "mutation($input: ItemInput!){ upsertItem(input:$input){ id name display } }";
        var mBody = Gql(m, new { input = new { name = "alpha" } });
        var mResp = await client.PostAsync("/graphql", mBody);
        mResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var mDoc = await mResp.Content.ReadFromJsonAsync<JsonDocument>();
        mDoc!.RootElement.TryGetProperty("errors", out var _).Should().BeFalse();

        // list via GraphQL
        var q = "query($page:Int,$size:Int){ items(page:$page,size:$size){ totalCount items{ id name display } } }";
        var qBody = Gql(q, new { page = 1, size = 10 });
        var qResp = await client.PostAsync("/graphql", qBody);
        qResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var qDoc = await qResp.Content.ReadFromJsonAsync<JsonDocument>();
        qDoc!.RootElement.TryGetProperty("errors", out var _e).Should().BeFalse();
        var total = qDoc.RootElement.GetProperty("data").GetProperty("items").GetProperty("totalCount").GetInt32();
        total.Should().BeGreaterOrEqualTo(1);
        var arr = qDoc.RootElement.GetProperty("data").GetProperty("items").GetProperty("items");
        arr.ValueKind.Should().Be(JsonValueKind.Array);
        arr.GetArrayLength().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task filter_query_should_return_subset()
    {
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
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>();
        doc!.RootElement.TryGetProperty("errors", out var _).Should().BeFalse();
        var data = doc.RootElement.GetProperty("data").GetProperty("items");
        data.GetProperty("totalCount").GetInt32().Should().BeGreaterOrEqualTo(1);
        var arr = data.GetProperty("items");
        arr.ValueKind.Should().Be(JsonValueKind.Array);
        arr.EnumerateArray().All(e => e.GetProperty("name").GetString()!.Contains('1')).Should().BeTrue();
    }
}
