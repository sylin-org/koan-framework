using System.Linq;
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
/// SEC-0005 — an <c>[Audit]</c> entity records one <see cref="AgentAction"/> per successful MUTATION (write/remove)
/// through the normal entity path; reads are never audited. Exercised end-to-end over the real REST path with a
/// UNIQUE agent id per test (isolation in the shared in-memory store).
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class AgentActionAuditE2ESpec
{
    private readonly RestEntityWebFactory _fx;

    public AgentActionAuditE2ESpec(RestEntityWebFactory fx) => _fx = fx;

    [Fact]
    public async Task A_write_records_exactly_one_audit_row()
    {
        const string agent = "audit-writer";
        (await Send(HttpMethod.Post, "/api/audited", agent, "{\"note\":\"x\"}")).IsSuccessStatusCode.Should().BeTrue();

        var actions = await AgentAction.Query(a => a.Subject == agent);
        var action = actions.Should().ContainSingle("exactly one audit row per mutation").Subject;
        action.Resource.Should().Be("Audited");
        action.Action.Should().Be("write");
        action.EntityId.Should().NotBeNullOrEmpty("a single write records the affected row id");
    }

    [Fact]
    public async Task A_read_is_never_audited()
    {
        const string agent = "audit-reader";
        (await Send(HttpMethod.Get, "/api/audited", agent)).IsSuccessStatusCode.Should().BeTrue();
        (await AgentAction.Query(a => a.Subject == agent)).Should().BeEmpty("reads are never audited (volume)");
    }

    [Fact]
    public async Task A_delete_records_a_remove_audit_row()
    {
        const string agent = "audit-deleter";
        var created = await Send(HttpMethod.Post, "/api/audited", agent, "{\"note\":\"y\"}");
        var id = JObject.Parse(await created.Content.ReadAsStringAsync())["id"]!.Value<string>();

        await Send(HttpMethod.Delete, $"/api/audited/{id}", agent);

        var actions = await AgentAction.Query(a => a.Subject == agent);
        actions.Should().HaveCount(2, "one write (create) + one remove");
        actions.Select(a => a.Action).Should().Contain("write").And.Contain("remove");
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string user, string? json = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-Test-User", user);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return _fx.Client.SendAsync(req);
    }
}
