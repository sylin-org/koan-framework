using System.Net;
using System.Text;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>AI-0042: every failure at the edge has one shape, the <c>Problem</c>. A refused call carries it in <c>structuredContent</c> and
/// <c>meta.problem</c>, whether the route refused or the edge did before any request; a value for the path or the query is the type the
/// route takes; and a refusal of the transport is a JSON-RPC error whose <c>data</c> is the problem.</summary>
public sealed class OneShapeSpec(ForumFixture host) : IClassFixture<ForumFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Problem(JToken result, int status, string kind, string? field = null)
    {
        result["isError"]!.Value<bool>().Should().BeTrue(result.ToString());
        foreach (var problem in new[] { result["structuredContent"], result["_meta"]!["problem"] })
        {
            problem!["status"]!.Value<int>().Should().Be(status, result.ToString());
            problem["kind"]!.Value<string>().Should().Be(kind);
            if (field is not null) problem["errors"]![field].Should().NotBeNull(result.ToString());
        }
        result["_meta"]!["status"]!.Value<int>().Should().Be(status);
        result["_meta"]!["errorCode"]!.Value<string>().Should().Be(kind);
    }

    [Fact]
    public async Task What_the_edge_refuses_before_any_request_is_a_problem_naming_the_field_and_nothing_landed()
    {
        var missing = await host.Call("reply", new JObject { ["place"] = "open", ["body"] = "No topic." }, "sam");
        Problem(missing, 400, "invalid", "topic");
        missing["_meta"]!["landed"]!.Value<bool>().Should().BeFalse();

        var dotted = await host.Call("name_get", new JObject { ["name"] = ".." }, "sam");
        Problem(dotted, 404, "absent");
    }

    [Theory]
    [InlineData("topic", 5)]
    [InlineData("topic", true)]
    [InlineData("place", null)]
    public async Task A_path_value_of_the_wrong_type_is_refused_on_its_field_and_never_read_as_a_string(string field, object? value)
    {
        var arguments = new JObject { ["place"] = "open", ["topic"] = "t-open", ["body"] = "Typed." };
        arguments[field] = value is null ? new JObject { ["nested"] = "x" } : JToken.FromObject(value);

        Problem(await host.Call("reply", arguments, "sam"), 400, "invalid", field);
    }

    [Fact]
    public async Task A_query_value_of_the_wrong_type_is_refused_on_its_field()
        => Problem(await host.Call("places_census_get", new JObject { ["place"] = "open", ["limit"] = "ten" }, "sam"), 400, "invalid", "limit");

    [Fact]
    public async Task A_route_refusal_keeps_its_own_problem()
    {
        var gated = await host.Call("reply", new JObject { ["place"] = "walled", ["topic"] = "t-walled", ["body"] = "Hello." }, null);
        Problem(gated, 401, "gated");
        gated["_meta"]!["landed"]!.Value<bool>().Should().BeFalse();
    }

    private async Task<(HttpStatusCode Status, JObject Body)> Post(string body, string contentType = "application/json", string? origin = null)
    {
        using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(body, Encoding.UTF8, contentType) };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        using var answer = await client.SendAsync(request, Ct);
        return (answer.StatusCode, JObject.Parse(await answer.Content.ReadAsStringAsync(Ct)));
    }

    private static void JsonRpcError(JObject body, int code, int status)
    {
        body["jsonrpc"]!.Value<string>().Should().Be("2.0", body.ToString());
        body["error"]!["code"]!.Value<int>().Should().Be(code);
        body["error"]!["data"]!["status"]!.Value<int>().Should().Be(status);
        body["error"]!["data"]!["kind"].Should().NotBeNull();
        body["error"]!["message"]!.Value<string>().Should().Be(body["error"]!["data"]!["detail"]!.Value<string>());
    }

    [Fact]
    public async Task A_refusal_of_the_transport_is_a_json_rpc_error_carrying_the_problem()
    {
        var notJson = await Post("{ not json");
        notJson.Status.Should().Be(HttpStatusCode.BadRequest);
        JsonRpcError(notJson.Body, -32700, 400);

        var notRequest = await Post("""{"jsonrpc":"1.0"}""");
        notRequest.Status.Should().Be(HttpStatusCode.BadRequest);
        JsonRpcError(notRequest.Body, -32600, 400);

        var form = await Post("a=b", "text/plain");
        form.Status.Should().Be(HttpStatusCode.UnsupportedMediaType);
        JsonRpcError(form.Body, -32000, 415);

        using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
        using var get = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        get.Headers.Accept.ParseAdd("application/json");
        using var stream = await client.SendAsync(get, Ct);
        stream.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        JsonRpcError(JObject.Parse(await stream.Content.ReadAsStringAsync(Ct)), -32000, 405);
    }

    [Fact]
    public async Task An_unknown_tool_is_the_specifications_error_carrying_an_absent_problem()
    {
        var unknown = await Post(new JObject { ["jsonrpc"] = "2.0", ["id"] = 7, ["method"] = "tools/call", ["params"] = new JObject { ["name"] = "nothing_here" } }.ToString());
        unknown.Body["id"]!.Value<int>().Should().Be(7);
        unknown.Body["error"]!["code"]!.Value<int>().Should().Be(-32602);
        unknown.Body["error"]!["data"]!["kind"]!.Value<string>().Should().Be("absent");
    }

    [Fact]
    public async Task A_browsers_origin_is_held_to_the_applications_own_and_a_request_with_none_passes()
    {
        var list = new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list" }.ToString();

        var foreign = await Post(list, origin: "http://evil.example");
        foreign.Status.Should().Be(HttpStatusCode.Forbidden);
        JsonRpcError(foreign.Body, -32000, 403);

        (await Post(list, origin: host.BaseUrl)).Status.Should().Be(HttpStatusCode.OK);
        (await Post(list)).Status.Should().Be(HttpStatusCode.OK);
    }
}
