using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Koan.Mcp.Options;
using Koan.Web.Authorization;
using Koan.Web.Extensions;
using Koan.Web.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>Who a request is: <c>Authorization: Test person;role=x;flagged=true</c>; none is a visitor. The header is the credential, which a tool call carries
/// to the route it reaches, so the route finds the caller as on a request of its own (SEC-0039).</summary>
internal sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "Test";
    private const string Prefix = "Test ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = Request.Headers.Authorization.ToString();
        if (!raw.StartsWith(Prefix, StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
        raw = raw[Prefix.Length..];
        if (raw == "rejected") return Task.FromResult(AuthenticateResult.Fail("A credential that does not verify."));
        var parts = raw.Split(';');
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, parts[0]), new("sub", parts[0]) };
        foreach (var part in parts.Skip(1))
        {
            var pair = part.Split('=', 2);
            claims.Add(pair[0] == "role" ? new Claim(ClaimTypes.Role, pair[1]) : new Claim(pair[0], pair[1]));
        }

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Name)), Name)));
    }
}

/// <summary>One answer of one door, for comparing.</summary>
public sealed record Answer(int Status, string? Kind, string Text, bool Failed, string? Location = null, string? Word = null, string? RetryAfter = null);

/// <summary>Middleware an application composed outside everything else: a tenant from a header, into a scoped service and <c>Items</c>, and what the
/// accessor holds when it gets the request back.</summary>
internal sealed class TenantMiddleware : IStartupFilter
{
    public static string? AccessorAfterMcp;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, inner) =>
        {
            var tenant = context.Request.Headers["X-Tenant"].ToString();
            context.RequestServices.GetRequiredService<TenantHolder>().Tenant = tenant;
            context.Items["tenant"] = tenant;
            await inner();
            if (context.Request.Path.StartsWithSegments("/mcp") && tenant.Length > 0)
                AccessorAfterMcp = context.RequestServices.GetRequiredService<IHttpContextAccessor>().HttpContext is null ? "null" : "present";
        });
        next(app);
    };
}

/// <summary>A real host on a real port and a SQLite file: every request is real HTTP, and every call a real <c>tools/call</c> over the
/// Streamable transport.</summary>
public class ForumFixture : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"koan-verbs-{Guid.CreateVersion7():n}.db");
    private IHost? _host;
    private int _port;

    public string BaseUrl => $"http://127.0.0.1:{_port}";

    /// <summary>Whether <see cref="TestAuth"/> is the default scheme, or the host's own schemes answer.</summary>
    protected virtual bool TestScheme => true;

    protected virtual Dictionary<string, string?> Settings() => new();

    protected virtual string EnvironmentName => "Development";

    protected virtual void Compose(IServiceCollection services) { }

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started.");

    public async ValueTask InitializeAsync()
    {
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={_path};Pooling=False",
                ["Koan:Mcp:Exposure"] = "Tools",
                ["Koan:Mcp:EnableStdioTransport"] = "false",
                ["Koan:Mcp:EnableStreamableHttpTransport"] = "true",
            }.Concat(Settings())))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls($"http://127.0.0.1:{_port}");
                web.UseEnvironment(EnvironmentName);
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddSingleton<IStartupFilter, TenantMiddleware>();
                    services.AddScoped<TenantHolder>();
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.AddKoan();
                    services.AddKoanWeb();
                    services.AddKoanControllersFrom<RepliesController>();
                    services.AddAuthorization(options => options.AddPolicy(Forum.NobodyPasses, policy => policy.RequireAssertion(_ => false)));
                    services.AddRateLimiter(options =>
                    {
                        options.RejectionStatusCode = 429;
                        options.AddFixedWindowLimiter(Forum.OnePermit, window => { window.PermitLimit = 1; window.Window = TimeSpan.FromHours(1); });
                    });
                    services.Configure<WebPipelineOptions>(pipeline => pipeline.UseRateLimiter = true);
                    if (TestScheme)
                    {
                        services.AddAuthentication(TestAuth.Name).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Name, _ => { });
                        services.PostConfigure<AuthenticationOptions>(options =>
                        {
                            options.DefaultScheme = options.DefaultAuthenticateScheme = options.DefaultChallengeScheme = options.DefaultForbidScheme = TestAuth.Name;
                        });
                    }

                    services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationProvider, CensusRule>());
                    Compose(services);
                });
                web.Configure(_ => { });
            })
            .Build();
        await _host.StartAsync(TestContext.Current.CancellationToken);

        using var app = AppHost.PushScope(_host.Services);
        var ct = TestContext.Current.CancellationToken;
        await new Place { Id = "open" }.Save(ct);
        await new Place { Id = "walled", Private = true, Members = ["mara"] }.Save(ct);
        await new Topic { Id = "t-open", PlaceId = "open" }.Save(ct);
        await new Topic { Id = "t-locked", PlaceId = "open", Locked = true }.Save(ct);
        await new Topic { Id = "t-walled", PlaceId = "walled" }.Save(ct);
        foreach (var person in new[] { "mara", "sam", "steward" }) await new Person { Id = person }.Save(ct);
        await new Person { Id = "ice", Frozen = true }.Save(ct);
        var roles = _host.Services.GetRequiredService<RoleCollection>();
        await roles.Define(Forum.Steward, "Steward", ct: ct);
        await roles.Define(Forum.Member, "Member", ct: ct);
        await roles.Define(Forum.Doomed, "Doomed", ct: ct);
        await roles.Add(Forum.Steward, "steward", ct: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    public IDisposable App() => AppHost.PushScope(Services);

    /// <summary>The text of an answer the door's client reads with no parse of its own.</summary>
    public async Task<string> RawBody(string name, JObject arguments, string? user)
    {
        using var client = Client(user);
        var ct = TestContext.Current.CancellationToken;
        using var response = await client.SendAsync(Envelope("tools/call", new JObject { ["name"] = name, ["arguments"] = arguments }), ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private HttpClient Client(string? user)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        if (user is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", user.StartsWith("Bearer ", StringComparison.Ordinal) ? user : "Test " + user);
        foreach (var (name, value) in Sent) client.DefaultRequestHeaders.Add(name, value);
        return client;
    }

    /// <summary>Headers every request of this flow carries as well as its credential.</summary>
    public Dictionary<string, string> Sent { get; } = new();

    /// <summary>A real request to the route.</summary>
    public async Task<Answer> Route(HttpMethod method, string path, JToken? json, string? user)
    {
        using var client = Client(user);
        using var request = new HttpRequestMessage(method, path);
        if (json is not null) request.Content = new StringContent(json.ToString(Formatting.None), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return Read((int)response.StatusCode, text) with { Location = response.Headers.Location?.ToString(), RetryAfter = response.Headers.RetryAfter?.ToString() };
    }

    /// <summary>A real form post to the route.</summary>
    public async Task<Answer> Form(string path, Dictionary<string, string> fields, string? user)
    {
        using var client = Client(user);
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(fields), TestContext.Current.CancellationToken);
        return Read((int)response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static Answer Read(int status, string text)
    {
        if (status < 400) return new Answer(status, null, text, false);
        if (text.Length == 0) return new Answer(status, null, "", true);
        var problem = JObject.Parse(text);
        var said = problem["errors"] is JObject errors
            ? string.Join(' ', errors.Properties().Select(field => $"{field.Name}: {string.Join(' ', field.Value.Values<string>())}"))
            : problem["detail"]?.Value<string>() ?? problem["title"]!.Value<string>()!;
        return new Answer(status, problem["kind"]?.Value<string>(), said, true, null, problem["word"]?.Value<string>());
    }

    /// <summary>A real <c>tools/call</c> over Streamable HTTP, a request of its own.</summary>
    public async Task<Answer> Tool(string name, JObject arguments, string? user)
    {
        var result = await Call(name, arguments, user);
        var text = result["content"]![0]!["text"]!.Value<string>()!;
        var meta = result["_meta"] as JObject ?? new JObject();
        return new Answer(meta["status"]?.Value<int>() ?? 0, meta["errorCode"]?.Value<string>(), text, result["isError"]?.Value<bool>() == true, meta["location"]?.Value<string>(),
            result["structuredContent"]?["word"]?.Value<string>(), meta["retryAfter"]?.Value<string>());
    }

    /// <summary>The whole result of a real <c>tools/call</c>.</summary>
    public Task<JToken> Call(string name, JObject arguments, string? user)
        => Rpc("tools/call", new JObject { ["name"] = name, ["arguments"] = arguments }, user);

    /// <summary>A real <c>tools/list</c>.</summary>
    public async Task<JObject[]> Tools(string? user)
        => ((JArray)(await Rpc("tools/list", new JObject(), user))["tools"]!).OfType<JObject>().ToArray();

    /// <summary>A real JSON-RPC request, answering its result.</summary>
    public async Task<JToken> Rpc(string method, JObject parameters, string? user)
    {
        using var client = Client(user);
        var ct = TestContext.Current.CancellationToken;
        using var response = await client.SendAsync(Envelope(method, parameters), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{method} answered {response.StatusCode}: {body} {response.Headers.WwwAuthenticate} on {client.BaseAddress}");
        var reply = JObject.Parse(body);
        return reply["result"] ?? throw new InvalidOperationException($"{method} answered {body}");
    }

    private static HttpRequestMessage Envelope(string method, JObject parameters)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters }.ToString(Formatting.None), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        return request;
    }
}
