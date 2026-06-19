using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Web.Authorization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0005 — governed agent access end-to-end over the real REST → gate path. A server-side <see cref="AgentGrant"/>
/// unlocks a gated action the caller's token alone is denied; <c>Remove()</c>/expiry revoke it on the next call.
/// The grant materializes as a scoped effective-claim and re-evaluates the SAME gate, so it is bound to the granted
/// capability AND resource — never a blanket bypass. Uses a UNIQUE agent id per test for isolation in the shared store.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class AgentGrantE2ESpec
{
    private readonly RestEntityWebFactory _fx;

    public AgentGrantE2ESpec(RestEntityWebFactory fx) => _fx = fx;

    [Fact]
    public async Task A_scope_grant_unlocks_a_gated_read_then_revocation_re_denies()
    {
        const string agent = "grant-reader";
        (await Read(agent)).Should().Be(HttpStatusCode.Forbidden, "no scope and no grant — the strongbox:read gate denies");

        var grant = new AgentGrant { Subject = agent, Capability = "has:scope:strongbox:read", Resource = "Strongbox" };
        await grant.Save();
        (await Read(agent)).Should().Be(HttpStatusCode.OK, "the grant materializes the strongbox:read scope");

        await grant.Remove();
        (await Read(agent)).Should().Be(HttpStatusCode.Forbidden, "Remove() revokes on the very next call (grants load fresh per request)");
    }

    [Fact]
    public async Task An_expired_grant_does_not_unlock()
    {
        const string agent = "grant-expired";
        await new AgentGrant
        {
            Subject = agent, Capability = "has:scope:strongbox:read", Resource = "Strongbox",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        }.Save();
        (await Read(agent)).Should().Be(HttpStatusCode.Forbidden, "an expired grant is inactive");
    }

    [Fact]
    public async Task A_grant_is_scoped_to_its_capability_and_resource()
    {
        const string agent = "grant-scoped";
        await new AgentGrant { Subject = agent, Capability = "has:scope:strongbox:read", Resource = "Strongbox" }.Save();
        (await Read(agent)).Should().Be(HttpStatusCode.OK, "the read grant unlocks read");
        (await Write(agent)).Should().Be(HttpStatusCode.Forbidden, "the read grant does NOT confer write (write needs strongbox:write)");

        const string other = "grant-other-resource";
        await new AgentGrant { Subject = other, Capability = "has:scope:strongbox:read", Resource = "Vault" }.Save();
        (await Read(other)).Should().Be(HttpStatusCode.Forbidden, "a grant scoped to resource 'Vault' does not apply to Strongbox");
    }

    [Fact]
    public async Task A_role_grant_unlocks_an_admin_gated_delete()
    {
        const string agent = "grant-admin";
        // Create a ledger row (write is open); then a non-admin delete is forbidden (remove gates on is:admin).
        var created = await Send(HttpMethod.Post, "/api/ledger", agent, "{\"entry\":\"x\"}");
        created.IsSuccessStatusCode.Should().BeTrue("create is open");
        var id = (JObject.Parse(await created.Content.ReadAsStringAsync()))["id"]!.Value<string>();

        (await Send(HttpMethod.Delete, $"/api/ledger/{id}", agent)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "a non-admin cannot delete (remove: is:admin)");

        await new AgentGrant { Subject = agent, Capability = "is:admin", Resource = "Ledger" }.Save();
        (await Send(HttpMethod.Delete, $"/api/ledger/{id}", agent)).StatusCode
            .Should().BeOneOf(new[] { HttpStatusCode.OK, HttpStatusCode.NoContent },
                "the is:admin grant materializes the admin role → delete allowed");
    }

    private async Task<HttpStatusCode> Read(string agent)
        => (await Send(HttpMethod.Get, "/api/strongbox", agent)).StatusCode;

    private async Task<HttpStatusCode> Write(string agent)
        => (await Send(HttpMethod.Post, "/api/strongbox", agent, "{\"contents\":\"x\"}")).StatusCode;

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string user, string? json = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-Test-User", user);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return _fx.Client.SendAsync(req);
    }
}
