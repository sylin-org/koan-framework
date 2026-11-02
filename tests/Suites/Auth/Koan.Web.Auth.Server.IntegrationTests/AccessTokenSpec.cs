using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Mcp;
using Koan.Web.Attributes;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Auth.Standing;
using Koan.Web.Authorization;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>A door where an account proved itself some other way; the query stands in for the proof.</summary>
public sealed class ProofDoorController : ControllerBase
{
    [HttpPost("/test/proof")]
    public async Task<IActionResult> Prove([FromQuery] string subject, [FromQuery] string? role, CancellationToken ct)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, "As proved") };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        return await Answer(new ClaimsPrincipal(new ClaimsIdentity(claims, "proof")), ct);
    }

    [HttpPost("/test/proof/nobody")]
    public Task<IActionResult> Nobody(CancellationToken ct)
        => Answer(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Nameless")], "proof")), ct);

    private async Task<IActionResult> Answer(ClaimsPrincipal principal, CancellationToken ct)
    {
        try
        {
            var token = await AccessToken.For(HttpContext, principal, ct);
            return token is null ? StatusCode(403) : Ok(new { token = token.Value, seconds = token.Lifetime.TotalSeconds });
        }
        catch (InvalidOperationException refused)
        {
            return BadRequest(refused.Message);
        }
    }

    [HttpGet("/test/proof/whoami"), Tool(Name = "proof_whoami")]
    public IActionResult WhoAmI() => Ok(new
    {
        id = User.FindFirstValue(ClaimTypes.NameIdentifier),
        name = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("name"),
        roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(),
    });

    [HttpPost("/test/proof/admin"), Tool(Name = "proof_admin"), Access(all: "is:admin")]
    public IActionResult Admin() => Ok(new { ran = true });

    [HttpPost("/test/proof/scoped"), Tool(Name = "proof_scoped"), RequireScope("notes.write")]
    public IActionResult Scoped() => Ok(new { ran = true });
}

/// <summary>A verb with no route: nothing on HTTP offers it.</summary>
public static class ProofVerbs
{
    [McpTool(Name = "proof_no_route", Description = "A verb no route offers.")]
    [Access(all: "is:admin")]
    public static string NoRoute() => "ran";
}

/// <summary>Who may act: one subject may not, and one holds a role their proof does not say.</summary>
internal sealed class ProofStanding : ISubjectStandingContributor
{
    public Task<SubjectStanding?> Read(string subject, CancellationToken ct = default) => Task.FromResult(subject switch
    {
        "gone" => new SubjectStanding(false),
        "promoted" => new SubjectStanding(true, "As they stand", ["keeper"]),
        _ => (SubjectStanding?)null,
    });
}

/// <summary>The OAuth host with the resource edge on, an MCP edge that accepts the application's bearer, and a short access-token lifetime.</summary>
public class AccessTokenFixture : IAsyncLifetime
{
    private IHost? _host;

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    protected virtual string EnvironmentName => "Development";

    protected virtual Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Environment"] = EnvironmentName,
        ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
        ["Koan:Data:Sources:Default:ConnectionString"] = "memory://access-token-tests",
        ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
        ["Koan:Mcp:EnableStreamableHttpTransport"] = "true",
        ["Koan:Mcp:EnableStdioTransport"] = "false",
        ["Koan:Mcp:Exposure"] = "Tools",
        ["Koan:Mcp:AcceptApplicationBearer"] = "true",
        ["Koan:BackgroundServices:Enabled"] = "false",
    };

    public async ValueTask InitializeAsync()
    {
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(Settings()))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment(EnvironmentName);
                web.ConfigureServices(s =>
                {
                    AppHost.Current = null;
                    s.AddKoan();
                    s.AddKoanWeb();
                    s.AddKoanControllersFrom<TestSignInController>();
                    s.AddSingleton<ISubjectStandingContributor, ProofStanding>();
                    s.Configure<AuthServerOptions>(o => o.AccessTokenLifetime = TimeSpan.FromMinutes(7));
                });
                web.Configure(_ => { });
            })
            .Build();
        await _host.StartAsync();
    }

    public HttpClient NewClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
    {
        BaseAddress = new Uri(BaseUrl),
    };

    public async Task<string> Proved(string subject, string? role = null, string? asHost = null)
    {
        using var client = NewClient();
        var path = $"/test/proof?subject={subject}" + (role is null ? "" : $"&role={role}");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (asHost is not null) request.Headers.Host = asHost;
        using var answer = await client.SendAsync(request, Ct);
        answer.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("seconds").GetDouble().Should().Be(420);
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    /// <summary>A real <c>tools/call</c> as <paramref name="token"/>: the JSON-RPC result.</summary>
    public async Task<JsonElement> Tool(string token, string name)
    {
        using var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var opened = await client.SendAsync(Rpc("initialize", new { protocolVersion = "2025-06-18" }, null), Ct);
        opened.StatusCode.Should().Be(HttpStatusCode.OK, opened.Headers.WwwAuthenticate.ToString());
        var session = opened.Headers.GetValues("Mcp-Session-Id").Single();
        using var called = await client.SendAsync(Rpc("tools/call", new { name, arguments = new { } }, session), Ct);
        called.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await called.Content.ReadAsStringAsync(Ct);
        var json = body.Split('\n').FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal)) is { } data ? data["data:".Length..] : body;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("result").Clone();
    }

    public async Task<HttpStatusCode> Route(string token, HttpMethod method, string path)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var answer = await client.SendAsync(request, Ct);
        return answer.StatusCode;
    }

    private static HttpRequestMessage Rpc(string method, object parameters, string? session)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (session is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        }

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}

/// <summary>
/// SEC-0037: an account the application verified by itself gets the token the server's flows mint, for the application's own resource, for the server's lifetime, as its standing
/// has it; and a token from the code flow for that resource is judged at the MCP edge as on the routes, whatever scope its client asked for.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class AccessTokenSpec(AccessTokenFixture host) : IClassFixture<AccessTokenFixture>
{
    [Fact]
    public async Task A_token_for_a_verified_account_names_the_application_and_lasts_as_long_as_the_servers()
    {
        var token = Read(await host.Proved("agent"));

        token.Audiences.Should().Equal(host.BaseUrl);
        token.Subject.Should().Be("agent");
        (token.ValidTo - token.ValidFrom).Should().Be(TimeSpan.FromMinutes(7));
        token.Issuer.Should().Be(host.BaseUrl, "the issuer the metadata states");
        token.Claims.Should().NotContain(claim => claim.Type == "client_id", "no client asked for it");
    }

    [Fact]
    public async Task It_is_good_on_the_routes_and_at_the_edge()
    {
        var token = await host.Proved("agent");

        (await host.Route(token, HttpMethod.Get, "/test/proof/whoami")).Should().Be(HttpStatusCode.OK);
        var called = await host.Tool(token, "proof_whoami");
        called.GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("\"id\":\"agent\"");
    }

    [Fact]
    public async Task It_carries_the_roles_it_was_given()
    {
        var token = await host.Proved("agent", "admin");

        (await host.Route(token, HttpMethod.Post, "/test/proof/admin")).Should().Be(HttpStatusCode.OK);
        (await host.Tool(token, "proof_admin")).GetProperty("isError").GetBoolean().Should().BeFalse();
    }

    /// <summary>What an application takes on by saying the edge is part of it: "a tool is a route" is true of <c>[Tool]</c> alone.</summary>
    [Fact]
    public async Task It_reaches_a_verb_no_route_offers_as_far_as_its_subject_may()
    {
        (await host.Tool(await host.Proved("agent", "admin"), "proof_no_route")).GetProperty("isError").GetBoolean().Should().BeFalse();
        (await host.Tool(await host.Proved("agent"), "proof_no_route")).GetProperty("isError").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_subject_who_may_not_act_gets_no_token()
    {
        using var client = host.NewClient();

        using var answer = await client.PostAsync("/test/proof?subject=gone", null, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Standing_replaces_what_the_proof_captured()
    {
        var token = await host.Proved("promoted", "admin");

        var called = await host.Tool(token, "proof_whoami");
        var said = called.GetProperty("content")[0].GetProperty("text").GetString();
        said.Should().Contain("keeper").And.NotContain("admin").And.Contain("As they stand");
    }

    [Fact]
    public async Task A_principal_with_no_subject_is_refused_saying_so()
    {
        using var client = host.NewClient();

        using var answer = await client.PostAsync("/test/proof/nobody", null, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await answer.Content.ReadAsStringAsync(Ct)).Should().Contain("no subject");
    }

    /// <summary>The person approves a client through the code flow, for the application, whatever it asks.</summary>
    private async Task<(HttpClient Browser, string Token)> Approved(string client, string scope, string? holding = null, string subject = "alice")
    {
        const string redirect = "http://127.0.0.1:7841/cb";
        await RegisterCodeClient(host.Services, client, redirect);
        var (verifier, challenge) = Pkce();
        var browser = host.NewClient();
        var signIn = $"/test/signin?sub={subject}" + (holding is null ? "" : $"&scope={Uri.EscapeDataString(holding)}");
        (await browser.GetAsync(signIn, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var authorize = await browser.GetAsync(ScopedAuthorize(client, redirect, challenge, scope), Ct);
        using var approved = await Decide(browser, "", Query(authorize.Headers.Location!.ToString(), "rid"), "approve");
        return (browser, await Redeem(browser, "", client, Query(approved.Headers.Location!.ToString(), "code"), verifier, redirect));
    }

    private string ScopedAuthorize(string client, string redirect, string challenge, string scope)
        => Authorize("", client, redirect, challenge, host.BaseUrl).Replace("scope=notes.read", "scope=" + Uri.EscapeDataString(scope));

    private static string? ScopeOf(string token) => Read(token).Claims.SingleOrDefault(claim => claim.Type == "scope")?.Value;

    [Fact]
    public async Task A_code_flow_token_for_the_application_is_judged_alike_and_a_scope_it_asked_for_opens_nothing()
    {
        var (browser, token) = await Approved("scope-walk", "notes.read");
        using var _ = browser;

        ScopeOf(token).Should().BeNull("the person holds no scope");

        // The person is an admin: a role opens the same door on the page, as a tool, and by their cookie.
        (await browser.PostAsync("/test/proof/admin", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Route(token, HttpMethod.Post, "/test/proof/admin")).Should().Be(HttpStatusCode.OK);
        (await host.Tool(token, "proof_admin")).GetProperty("isError").GetBoolean().Should().BeFalse();

        // A scope is not held on either door, nor by their cookie.
        (await browser.PostAsync("/test/proof/scoped", null, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.Route(token, HttpMethod.Post, "/test/proof/scoped")).Should().Be(HttpStatusCode.Forbidden);
        (await host.Tool(token, "proof_scoped")).GetProperty("isError").GetBoolean().Should().BeTrue();
    }

    /// <summary>D6: any signed-in person could approve their own consent for any scope, and the gate read it as held.</summary>
    [Fact]
    public async Task The_code_flow_grants_no_scope_the_person_lacks()
    {
        var (browser, token) = await Approved("scope-lacks", "notes.write anything:else");
        using var _ = browser;

        ScopeOf(token).Should().BeNull();
        (await host.Route(token, HttpMethod.Post, "/test/proof/scoped")).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_code_flow_grants_the_scope_the_person_holds_of_those_requested()
    {
        var (browser, token) = await Approved("scope-holds", "notes.write anything:else", holding: "notes.write other:one");
        using var _ = browser;

        ScopeOf(token).Should().Be("notes.write");
        (await browser.PostAsync("/test/proof/scoped", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Route(token, HttpMethod.Post, "/test/proof/scoped")).Should().Be(HttpStatusCode.OK);
        (await host.Tool(token, "proof_scoped")).GetProperty("isError").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("notes.write", "notes.write")]
    public async Task The_device_flow_grants_what_the_person_holds_of_what_was_requested(string? holding, string? granted)
    {
        var client = "device-scope-" + (holding is null ? "lacks" : "holds");
        await RegisterDeviceClient(host.Services, client);
        using var device = host.NewClient();
        using var started = await device.PostAsync("/oauth/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = client,
            ["scope"] = "notes.write anything:else",
            ["resource"] = host.BaseUrl,
        }), Ct);
        using var doc = JsonDocument.Parse(await started.Content.ReadAsStringAsync(Ct));
        var deviceCode = doc.RootElement.GetProperty("device_code").GetString()!;
        var userCode = doc.RootElement.GetProperty("user_code").GetString()!;
        using var browser = host.NewClient();
        (await browser.GetAsync("/test/signin" + (holding is null ? "" : $"?scope={holding}"), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var approved = await Decide(browser, "", userCode, "approve");

        var token = await PollForToken(device, "", client, deviceCode);

        ScopeOf(token).Should().Be(granted);
    }

    /// <summary>A grant's key names the scopes it holds, so asking after the requested ones would ask every time.</summary>
    [Fact]
    public async Task A_consent_to_fewer_scopes_than_requested_is_remembered()
    {
        const string redirect = "http://127.0.0.1:7841/cb";
        var (browser, _) = await Approved("scope-remembered", "notes.write anything:else", holding: "notes.write");
        using var __ = browser;

        var (verifier, challenge) = Pkce();
        using var again = await browser.GetAsync(ScopedAuthorize("scope-remembered", redirect, challenge, "notes.write anything:else"), Ct);

        again.Headers.Location!.ToString().Should().StartWith(redirect, "the person's consent is remembered");
        var token = await Redeem(browser, "", "scope-remembered", Query(again.Headers.Location!.ToString(), "code"), verifier, redirect);
        ScopeOf(token).Should().Be("notes.write", "the remembered path grants what the person holds too");
    }

    /// <summary>A person sees what approving gives, as they will be holding it, and until they sign in what was asked for.</summary>
    [Fact]
    public async Task The_consent_seam_says_what_approving_gives()
    {
        const string redirect = "http://127.0.0.1:7841/cb";
        await RegisterCodeClient(host.Services, "scope-shown", redirect);
        var (_, challenge) = Pkce();
        using var browser = host.NewClient();
        using var authorize = await browser.GetAsync(ScopedAuthorize("scope-shown", redirect, challenge, "notes.write anything:else"), Ct);
        var rid = Query(authorize.Headers.Location!.ToString(), "rid");

        async Task<string?[]> Shown()
        {
            using var doc = JsonDocument.Parse(await browser.GetStringAsync($"/oauth/request/{rid}", Ct));
            return doc.RootElement.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetProperty("id").GetString()).ToArray();
        }

        (await Shown()).Should().Equal("notes.write", "anything:else");
        (await browser.GetAsync("/test/signin?scope=notes.write", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Shown()).Should().Equal("notes.write");
    }

    /// <summary>The application's own word, and the door's: a principal that carries a scope hands it on.</summary>
    [Fact]
    public async Task A_grant_still_answers_at_the_gate_for_a_token_that_holds_no_scope()
    {
        var (browser, token) = await Approved("scope-granted", "notes.write", subject: "granted-person");
        using var _ = browser;
        ScopeOf(token).Should().BeNull();

        using (AppHost.PushScope(host.Services))
            await new AgentGrant { Subject = "granted-person", Capability = "has:scope:notes.write", Resource = "*", IssuedAt = DateTimeOffset.UtcNow }.Save(Ct);

        (await host.Route(token, HttpMethod.Post, "/test/proof/scoped")).Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>Outside Development with nothing naming the application: what Tangent is until a place has its address.</summary>
public sealed class UnaddressedAccessTokenFixture : AccessTokenFixture
{
    protected override string EnvironmentName => "Staging";

    protected override Dictionary<string, string?> Settings() => new(base.Settings())
    {
        ["Koan:Data:Sources:Default:ConnectionString"] = "memory://access-token-unaddressed",
    };
}

/// <summary>SEC-0037: outside Development a caller's <c>Host</c> does not choose a token's audience.</summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class UnaddressedAccessTokenSpec(UnaddressedAccessTokenFixture host) : IClassFixture<UnaddressedAccessTokenFixture>
{
    [Fact]
    public async Task With_nothing_naming_the_application_no_token_is_issued_and_the_refusal_says_what_to_do()
    {
        using var client = host.NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/test/proof?subject=agent");
        request.Headers.Host = "sibling.example";

        using var answer = await client.SendAsync(request, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await answer.Content.ReadAsStringAsync(Ct)).Should().Contain("Koan:Web:ApplicationUrl");
    }
}

/// <summary>Outside Development with the application's URL configured.</summary>
public sealed class AddressedAccessTokenFixture : AccessTokenFixture
{
    protected override string EnvironmentName => "Staging";

    protected override Dictionary<string, string?> Settings() => new(base.Settings())
    {
        ["Koan:Web:ApplicationUrl"] = BaseUrl,
        ["Koan:Data:Sources:Default:ConnectionString"] = "memory://access-token-addressed",
    };
}

/// <summary>SEC-0037: a configured URL names the application whatever the request's <c>Host</c> says.</summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class AddressedAccessTokenSpec(AddressedAccessTokenFixture host) : IClassFixture<AddressedAccessTokenFixture>
{
    [Fact]
    public async Task The_token_names_the_configured_url_and_not_the_host_it_was_asked_on()
    {
        var token = await host.Proved("agent", asHost: "sibling.example");

        Read(token).Audiences.Should().Equal(host.BaseUrl);
    }
}

/// <summary>An application whose ordinary routes name their resource themselves.</summary>
public sealed class NamedAccessTokenFixture : AccessTokenFixture
{
    public const string Resource = "urn:proof:application";

    protected override Dictionary<string, string?> Settings() => new(base.Settings())
    {
        ["Koan:Web:Auth:ResourceServer:Resource"] = Resource,
        ["Koan:Data:Sources:Default:ConnectionString"] = "memory://access-token-named",
    };
}

/// <summary>SEC-0037: the application's own resource is the one its routes accept, and not always its base URL.</summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class NamedAccessTokenSpec(NamedAccessTokenFixture host) : IClassFixture<NamedAccessTokenFixture>
{
    [Fact]
    public async Task The_token_names_the_resource_the_routes_accept()
    {
        var token = await host.Proved("agent");

        Read(token).Audiences.Should().Equal(NamedAccessTokenFixture.Resource);
        (await host.Route(token, HttpMethod.Get, "/test/proof/whoami")).Should().Be(HttpStatusCode.OK);
    }
}
