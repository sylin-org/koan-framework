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

/// <summary>Who a request is: <c>X-Test-User</c> names a person, with <c>;role=x</c> and <c>;flagged=true</c>. None is a visitor.</summary>
internal sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var raw)) return Task.FromResult(AuthenticateResult.NoResult());
        if (raw == "rejected") return Task.FromResult(AuthenticateResult.Fail("A credential that does not verify."));
        var parts = raw.ToString().Split(';');
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
public sealed record Answer(int Status, string? Kind, string Text, bool Failed, string? Location = null);

/// <summary>A real host on a real port and a SQLite file: every request is real HTTP, and every call a real <c>tools/call</c> over the
/// Streamable transport.</summary>
public sealed class ForumFixture : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"koan-verbs-{Guid.CreateVersion7():n}.db");
    private IHost? _host;
    private int _port;

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
            }))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls($"http://127.0.0.1:{_port}");
                web.UseEnvironment("Development");
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                    services.AddKoanWeb();
                    services.AddKoanControllersFrom<RepliesController>();
                    services.AddAuthentication(TestAuth.Name).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Name, _ => { });
                    services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationProvider, CensusRule>());
                    services.Configure<McpServerOptions>(options => options.Transport.StreamableJsonResponse = true);
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

    private HttpClient Client(string? user)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{_port}"), Timeout = TimeSpan.FromSeconds(60) };
        if (user is not null) client.DefaultRequestHeaders.Add(TestAuth.Header, user);
        return client;
    }

    /// <summary>A real request to the route.</summary>
    public async Task<Answer> Route(HttpMethod method, string path, JObject? json, string? user)
    {
        using var client = Client(user);
        using var request = new HttpRequestMessage(method, path);
        if (json is not null) request.Content = new StringContent(json.ToString(Formatting.None), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return Read((int)response.StatusCode, text) with { Location = response.Headers.Location?.ToString() };
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
        var problem = JObject.Parse(text);
        var said = problem["errors"] is JObject errors
            ? string.Join(' ', errors.Properties().Select(field => $"{field.Name}: {string.Join(' ', field.Value.Values<string>())}"))
            : problem["detail"]?.Value<string>() ?? problem["title"]!.Value<string>()!;
        return new Answer(status, problem["kind"]?.Value<string>(), said, true);
    }

    /// <summary>A real <c>tools/call</c> over Streamable HTTP, in a session of the caller's own.</summary>
    public async Task<Answer> Tool(string name, JObject arguments, string? user)
    {
        var result = await Rpc("tools/call", new JObject { ["name"] = name, ["arguments"] = arguments }, user);
        var text = result["content"]![0]!["text"]!.Value<string>()!;
        var meta = result["meta"] as JObject ?? new JObject();
        return new Answer(meta["status"]?.Value<int>() ?? 0, meta["errorCode"]?.Value<string>(), text, result["isError"]?.Value<bool>() == true, meta["location"]?.Value<string>());
    }

    /// <summary>A real <c>tools/list</c>.</summary>
    public async Task<JObject[]> Tools(string? user)
        => ((JArray)(await Rpc("tools/list", new JObject(), user))["tools"]!).OfType<JObject>().ToArray();

    private async Task<JToken> Rpc(string method, JObject parameters, string? user)
    {
        using var client = Client(user);
        var ct = TestContext.Current.CancellationToken;
        using var initialize = await client.SendAsync(Envelope("initialize", new JObject { ["protocolVersion"] = "2025-06-18" }, null), ct);
        initialize.EnsureSuccessStatusCode();
        var session = initialize.Headers.GetValues("Mcp-Session-Id").Single();
        using var response = await client.SendAsync(Envelope(method, parameters, session), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        var reply = JObject.Parse(body);
        return reply["result"] ?? throw new InvalidOperationException($"{method} answered {body}");
    }

    private static HttpRequestMessage Envelope(string method, JObject parameters, string? session)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters }.ToString(Formatting.None), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        if (session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        return request;
    }
}
