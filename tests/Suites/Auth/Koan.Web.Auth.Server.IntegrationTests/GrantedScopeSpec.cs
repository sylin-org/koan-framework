using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
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

/// <summary>A role's door and a scope's.</summary>
public sealed class ScopeDoorController : ControllerBase
{
    [HttpPost("/test/scope/admin"), Access(all: "is:admin")]
    public IActionResult Admin() => Ok(new { ran = true });

    [HttpPost("/test/scope/scoped"), RequireScope("notes.write")]
    public IActionResult Scoped() => Ok(new { ran = true });
}

/// <summary>The OAuth host with the resource edge on.</summary>
public sealed class GrantedScopeFixture : IAsyncLifetime
{
    private IHost? _host;

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");

    public async ValueTask InitializeAsync()
    {
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Development",
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Default:ConnectionString"] = "memory://granted-scope-tests",
                ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
                ["Koan:Mcp:EnableStdioTransport"] = "false",
                ["Koan:BackgroundServices:Enabled"] = "false",
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(s =>
                {
                    AppHost.Current = null;
                    s.AddKoan();
                    s.AddKoanWeb();
                    s.AddKoanControllersFrom<TestSignInController>();
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

    public async Task<HttpStatusCode> Route(string token, string path)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var answer = await client.SendAsync(request, Ct);
        return answer.StatusCode;
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

/// <summary>SEC-0006 D6, kept by SEC-0039 from SEC-0037: a token holds what was requested and what its person holds.</summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class GrantedScopeSpec(GrantedScopeFixture host) : IClassFixture<GrantedScopeFixture>
{
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
    public async Task A_code_flow_token_is_judged_as_its_persons_cookie_and_a_scope_it_asked_for_opens_nothing()
    {
        var (browser, token) = await Approved("scope-walk", "notes.read");
        using var _ = browser;

        ScopeOf(token).Should().BeNull("the person holds no scope");

        // The person is an admin: a role opens the same door to the token and to their cookie.
        (await browser.PostAsync("/test/scope/admin", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Route(token, "/test/scope/admin")).Should().Be(HttpStatusCode.OK);

        // A scope is held by neither.
        (await browser.PostAsync("/test/scope/scoped", null, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.Route(token, "/test/scope/scoped")).Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>D6: any signed-in person could approve their own consent for any scope, and the gate read it as held.</summary>
    [Fact]
    public async Task The_code_flow_grants_no_scope_the_person_lacks()
    {
        var (browser, token) = await Approved("scope-lacks", "notes.write anything:else");
        using var _ = browser;

        ScopeOf(token).Should().BeNull();
        (await host.Route(token, "/test/scope/scoped")).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_code_flow_grants_the_scope_the_person_holds_of_those_requested()
    {
        var (browser, token) = await Approved("scope-holds", "notes.write anything:else", holding: "notes.write other:one");
        using var _ = browser;

        ScopeOf(token).Should().Be("notes.write");
        (await browser.PostAsync("/test/scope/scoped", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Route(token, "/test/scope/scoped")).Should().Be(HttpStatusCode.OK);
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

        (await host.Route(token, "/test/scope/scoped")).Should().Be(HttpStatusCode.OK);
    }
}
