using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Mcp;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Explorer.IntegrationTests;

/// <summary>
/// WEB-0072 P1 — the Explorer console end-to-end: content-negotiated HTML, the anonymous-safe per-caller
/// surface (Verb / Door / Wall honesty), and the in-process try-it gated to authenticated callers.
/// </summary>
public sealed class ExplorerConsoleSpec : IClassFixture<ExplorerFixture>
{
    private readonly ExplorerFixture _fx;
    private readonly HttpClient _client;

    public ExplorerConsoleSpec(ExplorerFixture fx)
    {
        _fx = fx;
        _client = fx.NewClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // === D2: content negotiation ===

    [Fact]
    public async Task Console_is_served_only_to_a_browser_accept()
    {
        var html = await Get("/mcp", accept: "text/html,application/xhtml+xml,*/*;q=0.8");
        html.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await html.Content.ReadAsStringAsync(Ct)).Should().Contain("MCP Explorer");

        // An MCP client (advertises text/event-stream) is never intercepted; nor is a JSON client.
        (await Get("/mcp", accept: "text/event-stream")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Get("/mcp", accept: "application/json, text/event-stream")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Format_override_forces_html_even_without_a_browser_accept()
    {
        var res = await Get("/mcp?format=html", accept: "*/*");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task Static_assets_are_served_vendored()
    {
        var js = await Get("/mcp/explorer/app.js");
        js.StatusCode.Should().Be(HttpStatusCode.OK);
        js.Content.Headers.ContentType!.MediaType.Should().Be("application/javascript");
        (await js.Content.ReadAsStringAsync(Ct)).Should().Contain("map.json");

        var css = await Get("/mcp/explorer/explorer.css");
        css.StatusCode.Should().Be(HttpStatusCode.OK);
        css.Content.Headers.ContentType!.MediaType.Should().Be("text/css");
    }

    // === D3/D5: the per-caller surface (anonymous-safe), Verb / Door / Wall ===

    [Fact]
    public async Task MapJson_anonymous_shows_verb_door_and_hides_walls()
    {
        var map = await GetJson("/mcp/map.json");
        var entities = (JArray)map["entities"]!;

        // Verb: the public entity is visible with callable tools.
        var trinket = Entity(entities, "trinket");
        trinket.Should().NotBeNull();
        ((JArray)trinket!["tools"]!).Count.Should().BeGreaterThan(0);

        // Door: the scope-gated [Door] entity is present, with disclosed doors and NO callable tools.
        var docvault = Entity(entities, "docvault");
        docvault.Should().NotBeNull();
        ((JArray)docvault!["tools"]!).Count.Should().Be(0);
        var doors = (JArray?)docvault["doors"];
        doors.Should().NotBeNull();
        doors!.Count.Should().BeGreaterThan(0);
        doors.Select(d => (string?)d["needs"]).Should().Contain(n => n != null && n.Contains("docs:read"));

        // Wall: the role-gated entity leaves no trace.
        Entity(entities, "adminlog").Should().BeNull();

        // Identity block is present, and the LLM-facing instructions are surfaced (declare-once from config).
        ((string?)map["identity"]!["name"]).Should().NotBeNullOrEmpty();
        ((string?)map["instructions"]).Should().Be(ExplorerFixture.TestInstructions);
    }

    [Fact]
    public async Task MapJson_with_the_scope_turns_the_door_into_verbs()
    {
        var map = await GetJson("/mcp/map.json", auth: "scope=docs:read");
        var docvault = Entity((JArray)map["entities"]!, "docvault")!;
        ((JArray)docvault["tools"]!).Count.Should().BeGreaterThan(0);
        var doors = (JArray?)docvault["doors"];
        (doors is null || doors.Count == 0).Should().BeTrue();
    }

    [Fact]
    public async Task MapJson_with_admin_role_reveals_the_wall()
    {
        var anon = await GetJson("/mcp/map.json");
        Entity((JArray)anon["entities"]!, "adminlog").Should().BeNull();

        var admin = await GetJson("/mcp/map.json", auth: "role=admin");
        Entity((JArray)admin["entities"]!, "adminlog").Should().NotBeNull();
    }

    // === D4: in-process try-it ===

    [Fact]
    public async Task Execute_requires_authentication()
    {
        var res = await PostCall(ToolName("trinket", EntityEndpointOperationKind.Collection), auth: null);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Execute_runs_in_process_as_the_caller()
    {
        var res = await PostCall(ToolName("trinket", EntityEndpointOperationKind.Collection), auth: "role=user");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JObject.Parse(await res.Content.ReadAsStringAsync(Ct));
        body["success"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Execute_denied_by_the_gate_short_circuits()
    {
        // Authenticated but WITHOUT docs:read — the [Access] gate denies the scoped entity.
        var res = await PostCall(ToolName("docvault", EntityEndpointOperationKind.Collection), auth: "role=user");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JObject.Parse(await res.Content.ReadAsStringAsync(Ct));

        var shortCircuited = body["shortCircuit"]!.Type != JTokenType.Null;
        var failed = body["success"]!.Value<bool>() == false;
        (shortCircuited || failed).Should().BeTrue("a denied call must not read as a successful, unconstrained result");
    }

    // === helpers ===

    private static JObject? Entity(JArray entities, string name)
        => entities.OfType<JObject>().FirstOrDefault(e => string.Equals((string?)e["name"], name, StringComparison.OrdinalIgnoreCase));

    private string ToolName(string entity, EntityEndpointOperationKind operation)
    {
        var registry = _fx.Services.GetRequiredService<McpEntityRegistry>();
        var registration = registry.Registrations.First(r => string.Equals(r.DisplayName, entity, StringComparison.OrdinalIgnoreCase));
        return registration.Tools.First(t => t.Operation == operation).Name;
    }

    private Task<HttpResponseMessage> Get(string path, string? accept = null, string? auth = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (accept is not null) req.Headers.TryAddWithoutValidation("Accept", accept);
        if (auth is not null) req.Headers.TryAddWithoutValidation(TestAuthHandler.HeaderName, auth);
        return _client.SendAsync(req, Ct);
    }

    private async Task<JObject> GetJson(string path, string? auth = null)
    {
        var res = await Get(path, accept: "application/json", auth: auth);
        res.EnsureSuccessStatusCode();
        return JObject.Parse(await res.Content.ReadAsStringAsync(Ct));
    }

    private Task<HttpResponseMessage> PostCall(string name, string? auth)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/mcp/explorer/call");
        if (auth is not null) req.Headers.TryAddWithoutValidation(TestAuthHandler.HeaderName, auth);
        var body = new JObject { ["name"] = name, ["arguments"] = new JObject() };
        req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
        return _client.SendAsync(req, Ct);
    }
}
