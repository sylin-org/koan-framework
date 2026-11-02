using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AwesomeAssertions;
using Koan.Security.Trust.Issuer;
using Koan.Web.Attributes;
using Koan.Web.Auth.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>A cookie session, for a request that carries nothing else.</summary>
public sealed class CookieSignInController : ControllerBase
{
    [HttpGet("/bearer-spec/signin")]
    public async Task<IActionResult> SignIn([FromQuery] string subject)
    {
        await HttpContext.SignInAsync(AuthenticationExtensions.CookieScheme, new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, subject), new Claim("sub", subject)], AuthenticationExtensions.CookieScheme)));
        return Ok();
    }

    [HttpPost("/bearer-spec/signout")]
    public async Task<IActionResult> Leave()
    {
        await HttpContext.SignOutAsync(AuthenticationExtensions.CookieScheme);
        return Ok();
    }
}

public sealed record ListsInput(string[]? Permissions, List<string>? Others);

public sealed record OptionalListsInput(string[]? Permissions = null, List<string>? Others = null, string? Words = null);

public sealed record LimitedInput(string? Words);

/// <summary>What a tool call meets of a request, and what binding makes of a body.</summary>
public sealed class ToolRequestController : ControllerBase
{
    [HttpGet("/bearer-spec/scheme"), Tool(Name = "bearer_scheme")]
    public IActionResult Scheme() => Ok(new { scheme = HttpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Ticket?.AuthenticationScheme });

    [HttpPost("/bearer-spec/lists"), Tool(Name = "bearer_lists")]
    public IActionResult Lists(ListsInput input, string[]? plain) => Ok(new { permissions = input.Permissions?.Length, others = input.Others?.Count, plain = plain?.Length });

    [HttpPost("/bearer-spec/optional-lists"), Tool(Name = "bearer_optional_lists")]
    public IActionResult OptionalLists(OptionalListsInput input) => Ok(new { permissions = input.Permissions?.Length, others = input.Others?.Count });

    [HttpPost("/bearer-spec/nothing"), Tool(Name = "bearer_nothing")]
    public IActionResult Nothing(string? why) => Ok(new { why });

    [HttpGet("/bearer-spec/read"), Tool(Name = "bearer_read")]
    public IActionResult Read(string? q) => Ok(new { q });

    [HttpPost("/bearer-spec/limited"), Tool(Name = "bearer_limited"), RequestSizeLimit(64)]
    public IActionResult Limited(LimitedInput input) => Ok(new { words = input.Words });

    [HttpPost("/bearer-spec/limited-body"), Tool(Name = "bearer_limited_body"), RequestSizeLimit(64)]
    public IActionResult LimitedBody([FromBody] LimitedInput input) => Ok(new { words = input.Words });

    [HttpGet("/bearer-spec/both"), HttpHead("/bearer-spec/both"), Tool]
    public IActionResult Both() => Ok(new { read = true });
}

/// <summary>A host that says its MCP edge is part of it, and nothing else about the edge: not even that it requires authentication.</summary>
public class ApplicationBearerFixture : ForumFixture
{
    protected override bool TestScheme => false;

    protected override Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Mcp:AcceptApplicationBearer"] = "true",
        ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
    };

    public string Token(string audience, string subject = "bob", string[]? roles = null, string[]? scopes = null)
        => "Bearer " + Services.GetRequiredService<IIssuer>().Issue(
            new TrustClaims { Subject = subject, Roles = roles ?? [], Scopes = scopes ?? [] }, TimeSpan.FromMinutes(5), audience);

    /// <summary>A token signed by this host's own key, under an issuer of the spec's choosing.</summary>
    public string Signed(string issuer, string audience, bool expired = false)
    {
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        return "Bearer " + handler.WriteToken(handler.CreateJwtSecurityToken(
            issuer: issuer,
            audience: audience,
            subject: new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, "bob")]),
            notBefore: expired ? now.AddMinutes(-30) : now.AddMinutes(-1),
            expires: expired ? now.AddMinutes(-10) : now.AddMinutes(5),
            issuedAt: expired ? now.AddMinutes(-30) : now,
            signingCredentials: new SigningCredentials(Services.GetRequiredService<IIssuerKeyStore>().GetKeyRing().Active, EcdsaKeyRing.Algorithm)));
    }

    public string Metadata => $"{BaseUrl}/.well-known/oauth-protected-resource/mcp";

    public HttpClient Browser() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>One message to <c>/mcp</c>, with exactly the credential and session given.</summary>
    public static HttpRequestMessage Message(string method, string? credential = null, string? session = null, HttpMethod? verb = null)
    {
        var request = new HttpRequestMessage(verb ?? HttpMethod.Post, "/mcp");
        if (verb is null || verb == HttpMethod.Post)
        {
            request.Content = new StringContent(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = method,
                ["params"] = method == "initialize" ? new JObject { ["protocolVersion"] = "2025-06-18" } : new JObject(),
            }.ToString(), Encoding.UTF8, "application/json");
            request.Headers.Accept.ParseAdd("application/json");
        }

        request.Headers.Accept.ParseAdd("text/event-stream");
        if (credential is not null) request.Headers.TryAddWithoutValidation("Authorization", credential);
        if (session is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        }

        return request;
    }

    public async Task<string> Open(HttpClient client, string credential)
    {
        using var opened = await client.SendAsync(Message("initialize", credential), TestContext.Current.CancellationToken);
        opened.StatusCode.Should().Be(HttpStatusCode.OK, opened.Headers.WwwAuthenticate.ToString());
        return opened.Headers.GetValues("Mcp-Session-Id").Single();
    }
}

/// <summary>
/// SEC-0037: an application whose MCP edge is part of it. A token for the application is good at the edge as it is on the routes, and is judged alike on both doors; a token
/// for anything else is refused, with the resource edge reading the same header on the same request; no credential is the challenge a standard client starts from, and an
/// ambient one is no credential; a session stays its subject's.
/// </summary>
public sealed class ApplicationBearerSpec(ApplicationBearerFixture host) : IClassFixture<ApplicationBearerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_token_for_the_application_reaches_a_route_tool_as_its_subject()
    {
        var tool = await host.Tool("whoami_get", new JObject(), host.Token(host.BaseUrl));
        var route = await host.Route(HttpMethod.Get, "/fidelity/whoami", null, host.Token(host.BaseUrl));

        tool.Failed.Should().BeFalse(tool.ToString());
        JObject.Parse(tool.Text)["id"]!.Value<string>().Should().Be("bob");
        tool.Text.Should().Be(route.Text);
    }

    [Fact]
    public async Task A_tool_call_reads_as_the_bearer_that_established_it_on_either_door()
    {
        var token = host.Token(host.BaseUrl);

        var tool = await host.Tool("bearer_scheme", new JObject(), token);
        var route = await host.Route(HttpMethod.Get, "/bearer-spec/scheme", null, token);

        JObject.Parse(tool.Text)["scheme"]!.Value<string>().Should().Be("Koan.bearer", tool.ToString());
        tool.Text.Should().Be(route.Text);
    }

    [Fact]
    public async Task A_tool_call_with_a_cookie_beside_its_bearer_reads_as_the_bearer()
    {
        var ct = Ct;
        using var browser = host.Browser();
        (await browser.GetAsync("/bearer-spec/signin?subject=bob", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await host.Open(browser, host.Token(host.BaseUrl));
        using var call = ApplicationBearerFixture.Message("tools/call", host.Token(host.BaseUrl), session);
        call.Content = new StringContent(new JObject
        {
            ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/call", ["params"] = new JObject { ["name"] = "bearer_scheme", ["arguments"] = new JObject() },
        }.ToString(), Encoding.UTF8, "application/json");

        using var answer = await browser.SendAsync(call, ct);

        (await answer.Content.ReadAsStringAsync(ct)).Should().Contain("Koan.bearer");
    }

    [Theory]
    [InlineData("{\"permissions\":[],\"others\":[],\"plain\":[]}", 0, 0, 0)]
    [InlineData("{\"permissions\":[\"a\",\"b\"],\"others\":[\"a\"],\"plain\":[\"a\",\"b\",\"c\"]}", 2, 1, 3)]
    [InlineData("{\"permissions\":[]}", 0, null, 0)] // a parameter that is itself a collection binds empty whenever it is left out, as ASP.NET binds it
    public async Task An_empty_array_binds_empty_and_is_not_a_field_left_out_on_either_door(string body, int? permissions, int? others, int? plain)
    {
        var token = host.Token(host.BaseUrl);

        var route = await host.Route(HttpMethod.Post, "/bearer-spec/lists", JObject.Parse(body), token);
        var tool = await host.Tool("bearer_lists", JObject.Parse(body), token);

        foreach (var answer in new[] { route, tool })
        {
            var read = JObject.Parse(answer.Text);
            (read["permissions"]?.Value<int?>()).Should().Be(permissions, answer.ToString());
            (read["others"]?.Value<int?>()).Should().Be(others, answer.ToString());
            (read["plain"]?.Value<int?>()).Should().Be(plain, answer.ToString());
        }
    }

    /// <summary>= null is how an optional field on these inputs is written.</summary>
    [Theory]
    [InlineData("{\"permissions\":[],\"others\":[]}", 0, 0)]
    [InlineData("{\"permissions\":[\"a\",\"b\"]}", 2, null)]
    [InlineData("{\"words\":\"x\"}", null, null)]
    public async Task An_empty_array_binds_empty_where_the_field_has_a_default_on_either_door(string body, int? permissions, int? others)
    {
        var token = host.Token(host.BaseUrl);

        var route = await host.Route(HttpMethod.Post, "/bearer-spec/optional-lists", JObject.Parse(body), token);
        var tool = await host.Tool("bearer_optional_lists", JObject.Parse(body), token);

        foreach (var answer in new[] { route, tool })
        {
            var read = JObject.Parse(answer.Text);
            (read["permissions"]?.Value<int?>()).Should().Be(permissions, answer.ToString());
            (read["others"]?.Value<int?>()).Should().Be(others, answer.ToString());
        }
    }

    /// <summary>A wrapper that says JSON on every request. An action with no parameters never asks for its fields, so these have one.</summary>
    [Theory]
    [InlineData("GET", "/bearer-spec/read")]
    [InlineData("POST", "/bearer-spec/nothing")]
    public async Task A_json_content_type_with_no_body_reads_as_no_fields(string method, string path)
    {
        using var client = host.Browser();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = new ByteArrayContent([]) };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");

        using var answer = await client.SendAsync(request, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_form_past_the_limit_is_413_as_a_json_body_is()
    {
        using var client = host.Browser();
        using var form = new MultipartFormDataContent { { new StringContent(new string('x', 500)), "words" } };

        using var answer = await client.PostAsync("/bearer-spec/limited", form, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, await answer.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_form_under_the_limit_that_cannot_be_read_is_still_not_accepted()
    {
        using var client = host.Browser();
        using var cut = new ByteArrayContent(Encoding.ASCII.GetBytes("--b\r\nno"));
        cut.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=b");

        using var answer = await client.PostAsync("/bearer-spec/limited", cut, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest, await answer.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("/bearer-spec/limited", "bearer_limited")]
    [InlineData("/bearer-spec/limited-body", "bearer_limited_body")]
    public async Task A_body_past_the_limit_is_413_in_koans_refusal_on_either_door(string path, string name)
    {
        var token = host.Token(host.BaseUrl);
        var body = new JObject { ["words"] = new string('x', 500) };

        var route = await host.Route(HttpMethod.Post, path, body, token);
        var tool = await host.Tool(name, body, token);

        route.Status.Should().Be(413, route.ToString());
        route.Kind.Should().Be("invalid", route.ToString());
        tool.Status.Should().Be(413, tool.ToString());
        tool.Kind.Should().Be("invalid", tool.ToString());
    }

    [Fact]
    public async Task A_get_and_a_head_on_one_action_are_one_tool_and_both_stay_routes()
    {
        var token = host.Token(host.BaseUrl);
        using var client = host.Browser();
        using var head = new HttpRequestMessage(HttpMethod.Head, "/bearer-spec/both");

        var names = (await host.Tools(token)).Select(tool => tool["name"]!.Value<string>()).ToArray();
        using var answered = await client.SendAsync(head, Ct);

        names.Where(name => name!.StartsWith("bearer_spec_both", StringComparison.Ordinal)).Should().Equal("bearer_spec_both_get");
        answered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Tool("bearer_spec_both_get", new JObject(), token)).Failed.Should().BeFalse();
    }

    [Fact]
    public async Task An_application_that_exposes_tools_alone_gets_no_code_modes_sdk()
    {
        using var client = host.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/sdk/definitions");
        request.Headers.TryAddWithoutValidation("Authorization", host.Token(host.BaseUrl));

        using var answer = await client.SendAsync(request, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Services.GetRequiredService<Koan.Mcp.CodeMode.Sdk.ITypeScriptSdkProvider>().Current.Should().BeNull("nothing generated it, so nothing wrote it");
    }

    [Fact]
    public async Task A_token_for_the_application_with_its_trailing_slash_is_the_application()
    {
        var tool = await host.Tool("whoami_get", new JObject(), host.Token(host.BaseUrl + "/"));

        tool.Failed.Should().BeFalse(tool.ToString());
    }

    [Theory]
    [InlineData("/shelf/own", "shelf_own_post", new string[0], new string[0], 403)]
    [InlineData("/shelf/own", "shelf_own_post", new[] { "keeper" }, new string[0], 200)]
    [InlineData("/ledger/inherited", "ledger_inherited_post", new string[0], new string[0], 403)]
    [InlineData("/ledger/inherited", "ledger_inherited_post", new[] { "keeper" }, new string[0], 200)]
    [InlineData("/stack/scope", "stack_scope", new[] { "keeper" }, new string[0], 403)]
    [InlineData("/stack/scope", "stack_scope", new[] { "keeper" }, new[] { "x" }, 200)]
    [InlineData("/stack/scope", "stack_scope", new[] { "keeper" }, new[] { "mcp", "x" }, 200)]
    public async Task Authorize_access_and_a_scope_judge_the_token_alike_on_both_doors(string path, string name, string[] roles, string[] scopes, int status)
    {
        var token = host.Token(host.BaseUrl, roles: roles, scopes: scopes);

        var route = await host.Route(HttpMethod.Post, path, new JObject(), token);
        var tool = await host.Tool(name, new JObject(), token);

        route.Status.Should().Be(status, route.ToString());
        tool.Status.Should().Be(status, tool.ToString());
    }

    [Fact]
    public async Task A_scope_the_token_carries_narrows_nothing_a_role_opens()
    {
        var token = host.Token(host.BaseUrl, roles: ["keeper"], scopes: ["mcp"]);

        var route = await host.Route(HttpMethod.Post, "/shelf/own", new JObject(), token);
        var tool = await host.Tool("shelf_own_post", new JObject(), token);

        route.Status.Should().Be(200, route.ToString());
        tool.Status.Should().Be(200, tool.ToString());
    }

    [Fact]
    public async Task The_token_is_remote_at_the_edge_whatever_it_says()
    {
        var tool = await host.Tool("local_check_post", new JObject(), host.Token(host.BaseUrl));

        tool.Status.Should().Be(403, tool.ToString());
    }

    [Fact]
    public async Task A_token_for_the_edge_works_at_the_edge_and_on_no_route()
    {
        var tool = await host.Tool("whoami_get", new JObject(), host.Token($"{host.BaseUrl}/mcp"));
        var route = await host.Route(HttpMethod.Get, "/fidelity/whoami", null, host.Token($"{host.BaseUrl}/mcp"));

        tool.Failed.Should().BeFalse(tool.ToString());
        route.Status.Should().Be(401);
    }

    [Fact]
    public async Task The_edge_advertises_its_own_resource_so_a_strict_client_can_use_it()
    {
        using var client = host.Browser();

        var metadata = JObject.Parse(await client.GetStringAsync(host.Metadata, Ct));

        metadata["resource"]!.Value<string>().Should().Be($"{host.BaseUrl}/mcp");
        metadata["authorization_servers"]![0]!.Value<string>().Should().Be(host.BaseUrl);
    }

    /// <summary>The ordinary resource edge reads the same header first, on the same request.</summary>
    [Theory]
    [InlineData("a sibling resource")]
    [InlineData("the issuer's default audience")]
    [InlineData("another issuer")]
    [InlineData("expired")]
    [InlineData("tampered")]
    [InlineData("no token")]
    public async Task A_token_for_anything_else_is_refused_with_the_metadata(string kind)
    {
        var valid = host.Token(host.BaseUrl);
        var credential = kind switch
        {
            "a sibling resource" => host.Token("https://sibling.example"),
            "the issuer's default audience" => "Bearer " + host.Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = "bob" }),
            "another issuer" => host.Signed("https://elsewhere.example", host.BaseUrl),
            "expired" => host.Signed(new JwtSecurityTokenHandler().ReadJwtToken(valid["Bearer ".Length..]).Issuer, host.BaseUrl, expired: true),
            "tampered" => valid[..^6] + (valid[^6] == 'A' ? "BBBBBB" : "AAAAAA"),
            _ => "Bearer not-a-token",
        };
        using var client = host.Browser();

        using var answer = await client.SendAsync(ApplicationBearerFixture.Message("initialize", credential), Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        answer.Headers.WwwAuthenticate.ToString().Should().Be($"Bearer error=\"invalid_token\", resource_metadata=\"{host.Metadata}\"");
    }

    [Fact]
    public async Task No_credential_is_the_challenge_though_nothing_said_authentication_is_required()
    {
        using var client = host.Browser();

        using var answer = await client.SendAsync(ApplicationBearerFixture.Message("initialize"), Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        answer.Headers.WwwAuthenticate.ToString().Should().Be($"Bearer resource_metadata=\"{host.Metadata}\"");
    }

    [Fact]
    public async Task A_header_of_another_scheme_is_no_credential()
    {
        using var client = host.Browser();

        using var answer = await client.SendAsync(ApplicationBearerFixture.Message("initialize", "Basic Ym9iOmJvYg=="), Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cookie_never_authenticates_a_request_and_a_rejected_bearer_does_not_fall_back_to_it()
    {
        using var browser = host.Browser();
        (await browser.GetAsync("/bearer-spec/signin?subject=bob", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await browser.GetAsync("/fidelity/whoami", Ct);
        page.StatusCode.Should().Be(HttpStatusCode.OK, "the cookie is the person on the pages");

        using var post = await browser.SendAsync(ApplicationBearerFixture.Message("initialize"), Ct);
        using var stream = await browser.SendAsync(ApplicationBearerFixture.Message("", verb: HttpMethod.Get), HttpCompletionOption.ResponseHeadersRead, Ct);
        using var rejected = await browser.SendAsync(ApplicationBearerFixture.Message("initialize", "Bearer not-a-token"), Ct);

        post.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        stream.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cookie_does_not_drive_a_session_its_subject_opened()
    {
        using var browser = host.Browser();
        (await browser.GetAsync("/bearer-spec/signin?subject=bob", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await host.Open(browser, host.Token(host.BaseUrl));

        using var answer = await browser.SendAsync(ApplicationBearerFixture.Message("tools/list", session: session), Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_session_stays_its_subjects_whichever_of_their_tokens_they_present()
    {
        using var client = host.Browser();
        var session = await host.Open(client, host.Token(host.BaseUrl));

        using var another = await client.SendAsync(ApplicationBearerFixture.Message("tools/list", host.Token(host.BaseUrl, subject: "eve"), session), Ct);
        using var anotherEnds = await client.SendAsync(ApplicationBearerFixture.Message("", host.Token(host.BaseUrl, subject: "eve"), session, HttpMethod.Delete), Ct);
        using var same = await client.SendAsync(ApplicationBearerFixture.Message("tools/list", host.Token(host.BaseUrl), session), Ct);
        using var narrower = await client.SendAsync(ApplicationBearerFixture.Message("tools/list", host.Token($"{host.BaseUrl}/mcp"), session), Ct);

        another.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anotherEnds.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        same.StatusCode.Should().Be(HttpStatusCode.OK);
        narrower.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>Outside Development with nothing naming the application: what Tangent is until a place has its address.</summary>
public sealed class UnaddressedFixture : ApplicationBearerFixture
{
    protected override string EnvironmentName => "Staging";
}

/// <summary>Outside Development with the application's URL configured.</summary>
public sealed class AddressedFixture : ApplicationBearerFixture
{
    protected override string EnvironmentName => "Staging";

    protected override Dictionary<string, string?> Settings() => new(base.Settings())
    {
        ["Koan:Web:ApplicationUrl"] = BaseUrl,
    };
}

/// <summary>SEC-0037: outside Development a request's <c>Host</c> names no application.</summary>
public sealed class UnaddressedEdgeSpec(UnaddressedFixture host) : IClassFixture<UnaddressedFixture>
{
    [Fact]
    public async Task A_token_for_the_host_a_request_claims_is_not_the_applications()
    {
        using var client = host.Browser();
        using var request = ApplicationBearerFixture.Message("initialize", host.Token("http://sibling.example"));
        request.Headers.Host = "sibling.example";

        using var answer = await client.SendAsync(request, TestContext.Current.CancellationToken);

        answer.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>A 401 would send a standard client through OAuth for a token no edge takes: it starts none, and is told why.</summary>
    [Theory]
    [InlineData("http://sibling.example/mcp", "sibling.example")]
    [InlineData("{base}/mcp", null)]
    [InlineData("{base}", null)]
    [InlineData(null, null)]
    public async Task With_nothing_naming_the_application_the_edge_says_so_and_starts_no_oauth(string? audience, string? claimed)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.Browser();
        using var request = ApplicationBearerFixture.Message("initialize", audience is null ? null : host.Token(audience.Replace("{base}", host.BaseUrl)));
        if (claimed is not null) request.Headers.Host = claimed;

        using var answer = await client.SendAsync(request, ct);

        answer.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        answer.Headers.WwwAuthenticate.Should().BeEmpty();
        (await answer.Content.ReadAsStringAsync(ct)).Should().Contain("Koan:Web:ApplicationUrl");
    }

    [Fact]
    public async Task On_the_pages_a_token_for_the_host_a_request_claims_is_refused_and_a_cookie_still_signs_in()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/fidelity/whoami");
        request.Headers.TryAddWithoutValidation("Authorization", host.Token("http://sibling.example"));
        request.Headers.Host = "sibling.example";

        using var refused = await client.SendAsync(request, ct);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refused.Headers.WwwAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");

        (await client.GetAsync("/bearer-spec/signin?subject=bob", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var page = await client.GetAsync("/fidelity/whoami", ct);
        JObject.Parse(await page.Content.ReadAsStringAsync(ct))["id"]!.Value<string>().Should().Be("bob");
    }
}

/// <summary>SEC-0037: a configured URL names the application whatever the request's <c>Host</c> says.</summary>
public sealed class AddressedEdgeSpec(AddressedFixture host) : IClassFixture<AddressedFixture>
{
    [Fact]
    public async Task The_configured_url_is_the_application_and_a_claimed_host_is_not()
    {
        using var client = host.Browser();
        using var spoofed = ApplicationBearerFixture.Message("initialize", host.Token("http://sibling.example"));
        spoofed.Headers.Host = "sibling.example";
        using var named = ApplicationBearerFixture.Message("initialize", host.Token(host.BaseUrl));
        named.Headers.Host = "sibling.example";

        using var spoofedEdge = ApplicationBearerFixture.Message("initialize", host.Token("http://sibling.example/mcp"));
        spoofedEdge.Headers.Host = "sibling.example";
        using var namedEdge = ApplicationBearerFixture.Message("initialize", host.Token($"{host.BaseUrl}/mcp"));
        namedEdge.Headers.Host = "sibling.example";

        using var refused = await client.SendAsync(spoofed, TestContext.Current.CancellationToken);
        using var refusedEdge = await client.SendAsync(spoofedEdge, TestContext.Current.CancellationToken);
        using var accepted = await client.SendAsync(named, TestContext.Current.CancellationToken);
        using var acceptedEdge = await client.SendAsync(namedEdge, TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refusedEdge.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        acceptedEdge.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task On_the_pages_the_configured_url_is_the_application_and_a_claimed_host_is_not()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.Browser();
        using var claimed = new HttpRequestMessage(HttpMethod.Get, "/fidelity/whoami");
        claimed.Headers.TryAddWithoutValidation("Authorization", host.Token("http://sibling.example"));
        claimed.Headers.Host = "sibling.example";
        using var named = new HttpRequestMessage(HttpMethod.Get, "/fidelity/whoami");
        named.Headers.TryAddWithoutValidation("Authorization", host.Token(host.BaseUrl));
        named.Headers.Host = "sibling.example";

        using var refused = await client.SendAsync(claimed, ct);
        using var accepted = await client.SendAsync(named, ct);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// SEC-0037: a browser sends its session cookie whoever asks, so one that no longer holds reads as a visitor, on a route that is a tool and on
/// an entity's, with the edge on and off. A bearer a caller presented stays refused.
/// </summary>
public abstract class StaleCookieSpecBase(ApplicationBearerFixture host)
{
    private const string Session = ".AspNetCore.Koan.cookie";

    protected ApplicationBearerFixture Forum => host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> SignedIn(string subject)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(host.BaseUrl) };
        using var answer = await client.GetAsync($"/bearer-spec/signin?subject={subject}", Ct);
        answer.StatusCode.Should().Be(HttpStatusCode.OK);
        return answer.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(Session, StringComparison.Ordinal)).Split(';')[0];
    }

    private async Task<(HttpStatusCode Status, string Text)> Read(string path, string cookie)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(host.BaseUrl) };
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var answer = await client.SendAsync(request, Ct);
        return (answer.StatusCode, await answer.Content.ReadAsStringAsync(Ct));
    }

    private async Task ReadsAsAVisitor(string cookie)
    {
        var tool = await Read("/fidelity/whoami", cookie);
        tool.Status.Should().Be(HttpStatusCode.OK, tool.Text);
        JObject.Parse(tool.Text)["id"]?.Value<string>().Should().BeNull();
        var entity = await Read("/data/articles", cookie);
        entity.Status.Should().Be(HttpStatusCode.OK, entity.Text);
    }

    [Fact]
    public async Task A_good_cookie_is_its_person()
    {
        var cookie = await SignedIn($"stale-{Guid.CreateVersion7():N}");

        var read = await Read("/fidelity/whoami", cookie);

        JObject.Parse(read.Text)["id"]!.Value<string>().Should().StartWith("stale-");
    }

    [Fact]
    public async Task A_tampered_cookie_reads_as_a_visitor()
    {
        var cookie = await SignedIn($"stale-{Guid.CreateVersion7():N}");

        await ReadsAsAVisitor(cookie[..^8] + "AAAAAAAA");
    }

    [Fact]
    public async Task A_cookie_replayed_after_sign_out_reads_as_a_visitor()
    {
        var cookie = await SignedIn($"stale-{Guid.CreateVersion7():N}");
        using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(host.BaseUrl) })
        using (var signOut = new HttpRequestMessage(HttpMethod.Post, "/bearer-spec/signout"))
        {
            signOut.Headers.TryAddWithoutValidation("Cookie", cookie);
            (await client.SendAsync(signOut, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await ReadsAsAVisitor(cookie);
    }

    [Fact]
    public async Task A_stale_cookie_on_a_gated_route_is_asked_to_sign_in_as_a_visitor_is()
    {
        var visitor = await host.Route(HttpMethod.Post, "/ledger/inherited", new JObject(), null);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(host.BaseUrl) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ledger/inherited") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("Cookie", $"{Session}=not-a-cookie");

        using var answer = await client.SendAsync(request, Ct);

        ((int)answer.StatusCode).Should().Be(visitor.Status);
    }
}

public sealed class StaleCookieWithTheEdgeSpec(ApplicationBearerFixture host) : StaleCookieSpecBase(host), IClassFixture<ApplicationBearerFixture>
{
    [Fact]
    public async Task A_rejected_bearer_stays_refused_on_a_tool_route_and_an_entitys()
    {
        var ct = TestContext.Current.CancellationToken;
        foreach (var path in new[] { "/fidelity/whoami", "/data/articles" })
        {
            using var client = Forum.Browser();
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer not-a-token");

            using var answer = await client.SendAsync(request, ct);

            answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
        }
    }
}

/// <summary>The same host with no resource edge: the session cookie is the default scheme itself.</summary>
public sealed class CookieDefaultFixture : ApplicationBearerFixture
{
    protected override Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Mcp:AcceptApplicationBearer"] = "true",
    };
}

public sealed class StaleCookieWithoutTheEdgeSpec(CookieDefaultFixture host) : StaleCookieSpecBase(host), IClassFixture<CookieDefaultFixture>;

/// <summary>Outside Development with only the routes' own resource named, an absolute URL on this origin.</summary>
public sealed class NamedResourceNoUrlFixture : ApplicationBearerFixture
{
    protected override string EnvironmentName => "Staging";

    public string Resource => BaseUrl + "/app";

    protected override Dictionary<string, string?> Settings() => new(base.Settings())
    {
        ["Koan:Web:Auth:ResourceServer:Resource"] = Resource,
    };
}

/// <summary>SEC-0037: what the metadata names is what the edge takes, or a client that follows it gets a token refused, again each time.</summary>
public sealed class NamedResourceNoUrlSpec(NamedResourceNoUrlFixture host) : IClassFixture<NamedResourceNoUrlFixture>
{
    [Fact]
    public async Task The_metadata_names_the_resource_the_edge_takes()
    {
        using var client = host.Browser();

        var metadata = JObject.Parse(await client.GetStringAsync(host.Metadata, TestContext.Current.CancellationToken));
        var tool = await host.Tool("whoami_get", new JObject(), host.Token(metadata["resource"]!.Value<string>()!));

        metadata["resource"]!.Value<string>().Should().Be(host.Resource);
        tool.Failed.Should().BeFalse(tool.ToString());
    }
}

/// <summary>Production with a fixed resource and no URL or consent: a token can pass, and a challenge has nowhere to point.</summary>
public sealed class FixedResourceInProductionFixture : ApplicationBearerFixture
{
    public const string Resource = "urn:forum:edge";

    protected override string EnvironmentName => "Production";

    protected override Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Mcp:ResourceUri"] = Resource,
        ["Koan:Mcp:RequireAuthentication"] = "true",
    };
}

public sealed class FixedResourceInProductionSpec(FixedResourceInProductionFixture host) : IClassFixture<FixedResourceInProductionFixture>
{
    [Fact]
    public async Task No_credential_is_told_why_and_a_token_for_the_resource_gets_past_the_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.Browser();

        using var none = await client.SendAsync(ApplicationBearerFixture.Message("initialize"), ct);
        using var token = await client.SendAsync(ApplicationBearerFixture.Message("initialize", host.Token(FixedResourceInProductionFixture.Resource)), ct);

        none.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await none.Content.ReadAsStringAsync(ct));
        (await none.Content.ReadAsStringAsync(ct)).Should().Contain("Koan:Web:ApplicationUrl");
        token.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable);
        token.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}

/// <summary>An application whose ordinary routes name their resource themselves.</summary>
public sealed class NamedResourceFixture : ApplicationBearerFixture
{
    public const string Resource = "urn:forum:application";

    protected override Dictionary<string, string?> Settings() => new(base.Settings())
    {
        ["Koan:Web:Auth:ResourceServer:Resource"] = Resource,
    };
}

/// <summary>SEC-0037: the application's resource at the edge is the one its routes accept, and not always its base URL.</summary>
public sealed class NamedResourceSpec(NamedResourceFixture host) : IClassFixture<NamedResourceFixture>
{
    [Fact]
    public async Task The_edge_accepts_what_the_routes_accept_and_not_the_base_url()
    {
        var tool = await host.Tool("whoami_get", new JObject(), host.Token(NamedResourceFixture.Resource));
        var route = await host.Route(HttpMethod.Get, "/fidelity/whoami", null, host.Token(NamedResourceFixture.Resource));
        using var client = host.Browser();
        using var baseUrl = await client.SendAsync(ApplicationBearerFixture.Message("initialize", host.Token(host.BaseUrl)), TestContext.Current.CancellationToken);

        tool.Failed.Should().BeFalse(tool.ToString());
        route.Status.Should().Be(200);
        baseUrl.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
