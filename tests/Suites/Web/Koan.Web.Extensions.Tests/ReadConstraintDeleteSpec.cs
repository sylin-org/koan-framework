using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0013: a realization that constrains only Read still bounds every delete. <see cref="JournalAccess"/> forgets
/// Delete; a caller cannot delete another owner's row by id, by id list or by query.
/// Each owner pair is unique per test for isolation in the shared store.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class ReadConstraintDeleteSpec
{
    private readonly RestEntityWebFactory _fx;

    public ReadConstraintDeleteSpec(RestEntityWebFactory fx) => _fx = fx;

    [Fact]
    public async Task Delete_by_id_of_a_row_outside_the_read_scope_is_404()
    {
        const string alice = "rc-del-alice", bob = "rc-del-bob";
        var bobId = await Create(bob, "bobs");

        (await Send(HttpMethod.Delete, $"/api/journal/{bobId}", alice)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"/api/journal/{bobId}", bob)).StatusCode.Should().Be(HttpStatusCode.OK, "bob's row survives");
    }

    [Fact]
    public async Task Delete_many_skips_ids_outside_the_read_scope()
    {
        const string alice = "rc-bulk-alice", bob = "rc-bulk-bob";
        var a = await Create(alice, "a");
        var b = await Create(bob, "b");

        var res = await Send(HttpMethod.Delete, "/api/journal/bulk", alice, $"[\"{a}\",\"{b}\"]");
        res.IsSuccessStatusCode.Should().BeTrue();
        (JObject.Parse(await res.Content.ReadAsStringAsync()))["deleted"]!.Value<int>().Should().Be(1);
        (await Send(HttpMethod.Get, $"/api/journal/{b}", bob)).StatusCode.Should().Be(HttpStatusCode.OK, "bob's smuggled id is untouched");
    }

    [Fact]
    public async Task Delete_by_query_is_bounded_to_the_read_scope()
    {
        const string alice = "rc-dbq-alice", bob = "rc-dbq-bob";
        await Create(alice, "rc-match");
        await Create(bob, "rc-match");

        var path = "/api/journal?q=" + System.Uri.EscapeDataString("{\"text\":\"rc-match\"}");
        (await Send(HttpMethod.Delete, path, alice)).IsSuccessStatusCode.Should().BeTrue();

        (await Arr(await Send(HttpMethod.Get, "/api/journal", bob))).Should().HaveCount(1, "bob's matching row survives");
    }

    private async Task<string> Create(string user, string text)
    {
        var res = await Send(HttpMethod.Post, "/api/journal", user, $"{{\"text\":\"{text}\"}}");
        res.IsSuccessStatusCode.Should().BeTrue($"create for {user} should succeed");
        var o = JObject.Parse(await res.Content.ReadAsStringAsync());
        return (string)(o["id"] ?? o["Id"])!;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string user, string? json = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-Test-User", user);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return _fx.Client.SendAsync(req);
    }

    private static async Task<JArray> Arr(HttpResponseMessage res) => JArray.Parse(await res.Content.ReadAsStringAsync());
}
