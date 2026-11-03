using System.Net;
using System.Security.Claims;
using System.Text;
using AwesomeAssertions;
using Koan.Security.Trust.Issuer;
using Koan.Web.Attributes;
using Koan.Web.Auth.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>A cookie session, for a request that carries nothing else.</summary>
public sealed class CookieSignInController : ControllerBase
{
    [HttpGet("/request-spec/signin")]
    public async Task<IActionResult> SignIn([FromQuery] string subject)
    {
        await HttpContext.SignInAsync(AuthenticationExtensions.CookieScheme, new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, subject), new Claim("sub", subject)], AuthenticationExtensions.CookieScheme)));
        return Ok();
    }

    [HttpPost("/request-spec/signout")]
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
    [HttpPost("/request-spec/lists"), Tool(Name = "request_lists")]
    public IActionResult Lists(ListsInput input, string[]? plain) => Ok(new { permissions = input.Permissions?.Length, others = input.Others?.Count, plain = plain?.Length });

    [HttpPost("/request-spec/optional-lists"), Tool(Name = "request_optional_lists")]
    public IActionResult OptionalLists(OptionalListsInput input) => Ok(new { permissions = input.Permissions?.Length, others = input.Others?.Count });

    [HttpPost("/request-spec/nothing"), Tool(Name = "request_nothing")]
    public IActionResult Nothing(string? why) => Ok(new { why });

    [HttpGet("/request-spec/read"), Tool(Name = "request_read")]
    public IActionResult Read(string? q) => Ok(new { q });

    [HttpPost("/request-spec/limited"), Tool(Name = "request_limited"), RequestSizeLimit(64)]
    public IActionResult Limited(LimitedInput input) => Ok(new { words = input.Words });

    [HttpPost("/request-spec/limited-body"), Tool(Name = "request_limited_body"), RequestSizeLimit(64)]
    public IActionResult LimitedBody([FromBody] LimitedInput input) => Ok(new { words = input.Words });

    [HttpGet("/request-spec/both"), HttpHead("/request-spec/both"), Tool]
    public IActionResult Both() => Ok(new { read = true });
}

/// <summary>A tool call is a request: what binding makes of a body, and what a body past its limit is answered, alike on both doors.</summary>
public sealed class ToolRequestSpec(ForumFixture host) : IClassFixture<ForumFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Plain() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(host.BaseUrl) };

    [Theory]
    [InlineData("{\"permissions\":[],\"others\":[],\"plain\":[]}", 0, 0, 0)]
    [InlineData("{\"permissions\":[\"a\",\"b\"],\"others\":[\"a\"],\"plain\":[\"a\",\"b\",\"c\"]}", 2, 1, 3)]
    [InlineData("{\"permissions\":[]}", 0, null, 0)] // a parameter that is itself a collection binds empty whenever it is left out, as ASP.NET binds it
    public async Task An_empty_array_binds_empty_and_is_not_a_field_left_out_on_either_door(string body, int? permissions, int? others, int? plain)
    {
        var route = await host.Route(HttpMethod.Post, "/request-spec/lists", JObject.Parse(body), "bob");
        var tool = await host.Tool("request_lists", JObject.Parse(body), "bob");

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
        var route = await host.Route(HttpMethod.Post, "/request-spec/optional-lists", JObject.Parse(body), "bob");
        var tool = await host.Tool("request_optional_lists", JObject.Parse(body), "bob");

        foreach (var answer in new[] { route, tool })
        {
            var read = JObject.Parse(answer.Text);
            (read["permissions"]?.Value<int?>()).Should().Be(permissions, answer.ToString());
            (read["others"]?.Value<int?>()).Should().Be(others, answer.ToString());
        }
    }

    /// <summary>A wrapper that says JSON on every request. An action with no parameters never asks for its fields, so these have one.</summary>
    [Theory]
    [InlineData("GET", "/request-spec/read")]
    [InlineData("POST", "/request-spec/nothing")]
    public async Task A_json_content_type_with_no_body_reads_as_no_fields(string method, string path)
    {
        using var client = Plain();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = new ByteArrayContent([]) };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");

        using var answer = await client.SendAsync(request, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_form_past_the_limit_is_413_as_a_json_body_is()
    {
        using var client = Plain();
        using var form = new MultipartFormDataContent { { new StringContent(new string('x', 500)), "words" } };

        using var answer = await client.PostAsync("/request-spec/limited", form, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, await answer.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_form_under_the_limit_that_cannot_be_read_is_still_not_accepted()
    {
        using var client = Plain();
        using var cut = new ByteArrayContent(Encoding.ASCII.GetBytes("--b\r\nno"));
        cut.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=b");

        using var answer = await client.PostAsync("/request-spec/limited", cut, Ct);

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest, await answer.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("/request-spec/limited", "request_limited")]
    [InlineData("/request-spec/limited-body", "request_limited_body")]
    public async Task A_body_past_the_limit_is_413_in_koans_refusal_on_either_door(string path, string name)
    {
        var body = new JObject { ["words"] = new string('x', 500) };

        var route = await host.Route(HttpMethod.Post, path, body, "bob");
        var tool = await host.Tool(name, body, "bob");

        route.Status.Should().Be(413, route.ToString());
        route.Kind.Should().Be("invalid", route.ToString());
        tool.Status.Should().Be(413, tool.ToString());
        tool.Kind.Should().Be("invalid", tool.ToString());
    }

    [Fact]
    public async Task A_get_and_a_head_on_one_action_are_one_tool_and_both_stay_routes()
    {
        using var client = Plain();
        using var head = new HttpRequestMessage(HttpMethod.Head, "/request-spec/both");

        var names = (await host.Tools("bob")).Select(tool => tool["name"]!.Value<string>()).ToArray();
        using var answered = await client.SendAsync(head, Ct);

        names.Where(name => name!.StartsWith("request_spec_both", StringComparison.Ordinal)).Should().Equal("request_spec_both_get");
        answered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Tool("request_spec_both_get", new JObject(), "bob")).Failed.Should().BeFalse();
    }
}

/// <summary>The host's own schemes, with the resource edge for ordinary requests on.</summary>
public class ResourceEdgeFixture : ForumFixture
{
    protected override bool TestScheme => false;

    protected override Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
    };

    public string Token(string audience, string subject = "bob")
        => "Bearer " + Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = subject }, TimeSpan.FromMinutes(5), audience);

    public HttpClient Browser() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(30),
    };
}

/// <summary>
/// A browser sends its session cookie whoever asks, so one that no longer holds reads as a visitor, on a route that is a tool and on an entity's, with the
/// resource edge on and off. A bearer a caller presented stays refused.
/// </summary>
public abstract class StaleCookieSpecBase(ResourceEdgeFixture host)
{
    private const string Session = ".AspNetCore.Koan.cookie";

    protected ResourceEdgeFixture Forum => host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> SignedIn(string subject)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(host.BaseUrl) };
        using var answer = await client.GetAsync($"/request-spec/signin?subject={subject}", Ct);
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
        using (var signOut = new HttpRequestMessage(HttpMethod.Post, "/request-spec/signout"))
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

public sealed class StaleCookieWithTheEdgeSpec(ResourceEdgeFixture host) : StaleCookieSpecBase(host), IClassFixture<ResourceEdgeFixture>
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
public sealed class CookieDefaultFixture : ResourceEdgeFixture
{
    protected override Dictionary<string, string?> Settings() => new();
}

public sealed class StaleCookieWithoutTheEdgeSpec(CookieDefaultFixture host) : StaleCookieSpecBase(host), IClassFixture<CookieDefaultFixture>;

/// <summary>Outside Development with nothing naming the application.</summary>
public sealed class UnaddressedFixture : ResourceEdgeFixture
{
    protected override string EnvironmentName => "Staging";
}

/// <summary>Outside Development with the application's URL configured.</summary>
public sealed class AddressedFixture : ResourceEdgeFixture
{
    protected override string EnvironmentName => "Staging";

    protected override Dictionary<string, string?> Settings() => new(base.Settings())
    {
        ["Koan:Web:ApplicationUrl"] = BaseUrl,
    };
}

/// <summary>SEC-0039, from SEC-0037: outside Development a request's <c>Host</c> names no application.</summary>
public sealed class UnaddressedPagesSpec(UnaddressedFixture host) : IClassFixture<UnaddressedFixture>
{
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

        (await client.GetAsync("/request-spec/signin?subject=bob", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var page = await client.GetAsync("/fidelity/whoami", ct);
        JObject.Parse(await page.Content.ReadAsStringAsync(ct))["id"]!.Value<string>().Should().Be("bob");
    }
}

/// <summary>SEC-0039, from SEC-0037: a configured URL names the application whatever the request's <c>Host</c> says.</summary>
public sealed class AddressedPagesSpec(AddressedFixture host) : IClassFixture<AddressedFixture>
{
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
