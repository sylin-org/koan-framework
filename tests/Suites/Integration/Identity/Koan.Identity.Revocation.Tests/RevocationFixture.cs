using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Koan.Identity.Revocation.Tests;

/// <summary>
/// A real Kestrel host with Koan.Identity, the cookie session and the embedded authorization server over an in-memory
/// store: what a person holds, driven over real HTTP with real cookies and real tokens.
/// </summary>
public class RevocationFixture : IAsyncLifetime
{
    private IHost? _host;

    public string BaseUrl { get; private set; } = "";
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        listener.Stop();

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Development",
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Default:ConnectionString"] = "memory://identity-revocation",
                ["Koan:BackgroundServices:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning",
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(BaseUrl);
                web.UseEnvironment("Development");
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                    services.AddKoanWeb();
                    services.AddKoanControllersFrom<RevocationProbeController>();
                    Compose(services);
                });
                web.Configure(_ => { });
            })
            .Build();
        await _host.StartAsync(Ct);
    }

    /// <summary>What a derived fixture adds to the host.</summary>
    protected virtual void Compose(IServiceCollection services) { }

    public async ValueTask DisposeAsync()
    {
        if (_host is null) return;
        await _host.StopAsync();
        _host.Dispose();
    }

    /// <summary>A browser: its own cookies, following no redirect.</summary>
    public HttpClient Browser()
        => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
        {
            BaseAddress = new Uri(BaseUrl),
        };

    public static async Task SignIn(HttpClient browser, string person)
    {
        using var signedIn = await browser.GetAsync($"/probe/signin?person={Uri.EscapeDataString(person)}", Ct);
        signedIn.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The person, signed in on <paramref name="browser"/>, approves a new client: its code, not yet redeemed.</summary>
    public async Task<PendingCode> Approve(HttpClient browser, string clientId)
    {
        const string redirect = "http://127.0.0.1:7788/cb";
        await new OAuthClient { Id = clientId, ClientName = clientId, RedirectUris = [redirect], CreatedUtc = DateTimeOffset.UtcNow }.Save(Ct);
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        using var authorize = await browser.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirect,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = "notes.read",
            ["resource"] = $"{BaseUrl}/mcp",
        }), Ct);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{Query(authorize.Headers.Location!.ToString(), "rid")}/approve");
        approve.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var approved = await browser.SendAsync(approve, Ct);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(Ct));
        using var document = JsonDocument.Parse(await approved.Content.ReadAsStringAsync(Ct));
        return new PendingCode(clientId, Query(document.RootElement.GetProperty("redirect").GetString()!, "code"), verifier, redirect);
    }

    public static Task<HttpResponseMessage> Redeem(HttpClient http, PendingCode code)
        => http.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code.Code,
            ["redirect_uri"] = code.Redirect,
            ["client_id"] = code.ClientId,
            ["code_verifier"] = code.Verifier,
        }), Ct);

    /// <summary>The person approves a new client and the client redeems its code: the agent, connected.</summary>
    public async Task<Agent> Connect(HttpClient browser, string clientId)
    {
        var code = await Approve(browser, clientId);
        using var redeemed = await Redeem(browser, code);
        redeemed.StatusCode.Should().Be(HttpStatusCode.OK, await redeemed.Content.ReadAsStringAsync(Ct));
        return new Agent(clientId, await Tokens.Read(redeemed));
    }

    public static Task<HttpResponseMessage> Refresh(HttpClient http, string clientId, string refreshToken)
        => http.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }), Ct);

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Query(string url, string key)
        => QueryHelpers.ParseQuery(url[(url.IndexOf('?') + 1)..])[key].ToString();
}

public sealed record PendingCode(string ClientId, string Code, string Verifier, string Redirect);

public sealed record Agent(string ClientId, Tokens Tokens);

public sealed record Tokens(string Access, string Refresh)
{
    public static async Task<Tokens> Read(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(RevocationFixture.Ct));
        return new Tokens(document.RootElement.GetProperty("access_token").GetString()!, document.RootElement.GetProperty("refresh_token").GetString()!);
    }

    /// <summary>The roles the access token carries, in the claim type the issuer writes them.</summary>
    public string[] Roles => new JwtSecurityTokenHandler().ReadJwtToken(Access).Claims
        .Where(static claim => claim.Type is ClaimTypes.Role or "role" or "roles").Select(static claim => claim.Value).ToArray();

    /// <summary>The name the access token carries.</summary>
    public string? Name => new JwtSecurityTokenHandler().ReadJwtToken(Access).Claims
        .FirstOrDefault(static claim => claim.Type is JwtRegisteredClaimNames.Name or ClaimTypes.Name)?.Value;
}

/// <summary>The application's own surface in the probe host: an interactive sign-in stands in here, and one role's page.</summary>
[ApiController]
public sealed class RevocationProbeController : ControllerBase
{
    public const string GuardedRole = "role:guarded";

    [HttpGet("/probe/signin")]
    public async Task<IActionResult> SignIn([FromQuery] string person)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, person), new Claim(ClaimTypes.Name, "As signed in")],
            AuthenticationExtensions.CookieScheme);
        await HttpContext.SignInAsync(AuthenticationExtensions.CookieScheme, new ClaimsPrincipal(identity));
        return Ok();
    }

    [Authorize(Roles = GuardedRole)]
    [HttpGet("/probe/guarded")]
    public IActionResult Guarded() => Ok();
}
