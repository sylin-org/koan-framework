using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0004 Slice B — the Constrain end-to-end contract over the owned <see cref="Memo"/> entity (realization
/// <see cref="MemoAccess"/>): reads narrow to the owner, create stamps server-truth, update/delete of another's row
/// is a 404 (existence-hiding), ownership is frozen, and a mass delete is bounded. Each test uses a UNIQUE
/// <c>X-Test-User</c>, so the owner-narrowing itself isolates it in the shared in-memory store.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class EntityConstrainE2ESpec
{
    private readonly RestEntityWebFactory _fx;

    public EntityConstrainE2ESpec(RestEntityWebFactory fx) => _fx = fx;

    // ── the headline footgun tripwire ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Create_stamps_the_owner_overwriting_a_forged_value()
    {
        const string alice = "stamp-alice";
        // alice creates a memo but forges someone else's ownerId in the payload
        var res = await Send(HttpMethod.Post, "/api/memo", alice, "{\"ownerId\":\"victim\",\"text\":\"hi\"}");
        res.IsSuccessStatusCode.Should().BeTrue();
        Owner(await Obj(res)).Should().Be(alice, "the forged ownerId must be overwritten with server-truth (the principal), not persisted as-is");

        // and it is persisted that way: alice can read it back, the forged 'victim' cannot
        var id = Id(await Obj(res));
        (await Send(HttpMethod.Get, $"/api/memo/{id}", alice)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Get, $"/api/memo/{id}", "victim")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── reads narrow to the owner ────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Collection_reads_are_narrowed_to_the_owner()
    {
        const string alice = "read-alice", bob = "read-bob";
        await Create(alice, "a-memo");
        await Create(bob, "b-memo");

        var aliceRows = await Arr(await Send(HttpMethod.Get, "/api/memo", alice));
        aliceRows.Select(x => Owner((JObject)x)).Should().OnlyContain(o => o == alice);
        aliceRows.Select(x => Text((JObject)x)).Should().Contain("a-memo").And.NotContain("b-memo");
    }

    [Fact]
    public async Task Get_by_id_of_anothers_row_is_404()
    {
        const string alice = "byid-alice", bob = "byid-bob";
        var id = await Create(alice, "secret");
        (await Send(HttpMethod.Get, $"/api/memo/{id}", bob)).StatusCode.Should().Be(HttpStatusCode.NotFound, "out of scope is indistinguishable from missing");
        (await Send(HttpMethod.Get, $"/api/memo/{id}", alice)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_user_filter_cannot_widen_past_the_owner_constraint()
    {
        const string alice = "escape-alice", bob = "escape-bob";
        await Create(bob, "bobs-secret");
        // alice explicitly filters for bob's rows — the Constraint AND-composes, so she still sees nothing
        var path = "/api/memo?filter=" + Uri.EscapeDataString($"{{\"ownerId\":\"{bob}\"}}");
        (await Arr(await Send(HttpMethod.Get, path, alice))).Should().BeEmpty("the access constraint cannot be escaped by a user filter");
    }

    // ── update / delete of another's row is 404; ownership is frozen ─────────────────────────────────────────────
    [Fact]
    public async Task Update_of_anothers_row_is_404_and_ownership_is_frozen()
    {
        const string alice = "upd-alice", bob = "upd-bob";
        var aliceId = await Create(alice, "mine");
        var bobId = await Create(bob, "bobs");

        // alice cannot hijack bob's row (upsert with bob's id)
        var hijack = await Send(HttpMethod.Post, "/api/memo", alice, $"{{\"id\":\"{bobId}\",\"ownerId\":\"{alice}\",\"text\":\"hijack\"}}");
        hijack.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // alice updates her own row but cannot reassign the owner (freeze)
        var ok = await Send(HttpMethod.Post, "/api/memo", alice, $"{{\"id\":\"{aliceId}\",\"ownerId\":\"{bob}\",\"text\":\"edited\"}}");
        ok.IsSuccessStatusCode.Should().BeTrue();
        Owner(await Obj(ok)).Should().Be(alice, "ownership is frozen — a payload cannot reassign the owner");
    }

    [Fact]
    public async Task Delete_of_anothers_row_is_404()
    {
        const string alice = "del-alice", bob = "del-bob";
        var bobId = await Create(bob, "bobs");
        (await Send(HttpMethod.Delete, $"/api/memo/{bobId}", alice)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Delete, $"/api/memo/{bobId}", bob)).IsSuccessStatusCode.Should().BeTrue("the owner can delete their own row");
    }

    // ── mass deletes are bounded to the owner ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Delete_all_is_bounded_to_the_owners_rows()
    {
        const string alice = "mass-alice", bob = "mass-bob";
        await Create(alice, "a1");
        await Create(alice, "a2");
        await Create(bob, "b1");
        await Create(bob, "b2");

        (await Send(HttpMethod.Delete, "/api/memo/all", alice)).IsSuccessStatusCode.Should().BeTrue();

        (await Arr(await Send(HttpMethod.Get, "/api/memo", alice))).Should().BeEmpty("alice's rows are gone");
        (await Arr(await Send(HttpMethod.Get, "/api/memo", bob))).Should().HaveCount(2, "bob's rows survive a 'delete all' issued by alice");
    }

    [Fact]
    public async Task Delete_by_query_is_bounded_to_the_owner()
    {
        const string alice = "dbq-alice", bob = "dbq-bob";
        await Create(alice, "match");
        await Create(bob, "match");

        // alice deletes by a query matching BOTH her and bob's rows — the constraint AND-bounds it to hers
        var path = "/api/memo?q=" + Uri.EscapeDataString("{\"text\":\"match\"}");
        (await Send(HttpMethod.Delete, path, alice)).IsSuccessStatusCode.Should().BeTrue();

        (await Arr(await Send(HttpMethod.Get, "/api/memo", alice))).Should().BeEmpty("alice's matching row is deleted");
        (await Arr(await Send(HttpMethod.Get, "/api/memo", bob))).Should().HaveCount(1, "bob's matching row survives alice's delete-by-query");
    }

    [Fact]
    public async Task Delete_many_only_removes_owned_ids()
    {
        const string alice = "bulk-alice", bob = "bulk-bob";
        var a = await Create(alice, "a");
        var b = await Create(bob, "b");

        (await Send(HttpMethod.Delete, "/api/memo/bulk", alice, $"[\"{a}\",\"{b}\"]")).IsSuccessStatusCode.Should().BeTrue();

        (await Send(HttpMethod.Get, $"/api/memo/{a}", alice)).StatusCode.Should().Be(HttpStatusCode.NotFound, "alice's own id is deleted");
        (await Send(HttpMethod.Get, $"/api/memo/{b}", bob)).StatusCode.Should().Be(HttpStatusCode.OK, "bob's id, smuggled into alice's bulk delete, is untouched");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────
    private async Task<string> Create(string user, string text)
    {
        var res = await Send(HttpMethod.Post, "/api/memo", user, $"{{\"text\":\"{text}\"}}");
        res.IsSuccessStatusCode.Should().BeTrue($"create for {user} should succeed");
        return Id(await Obj(res));
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string user, string? json = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-Test-User", user);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return _fx.Client.SendAsync(req);
    }

    private static async Task<JObject> Obj(HttpResponseMessage res) => JObject.Parse(await res.Content.ReadAsStringAsync());
    private static async Task<JArray> Arr(HttpResponseMessage res) => JArray.Parse(await res.Content.ReadAsStringAsync());
    private static string? Owner(JObject o) => (string?)(o["ownerId"] ?? o["OwnerId"]);
    private static string Text(JObject o) => (string?)(o["text"] ?? o["Text"]) ?? "";
    private static string Id(JObject o) => (string)(o["id"] ?? o["Id"])!;
}
