using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// A loopback PLC directory, PDS and authorization server for any number of accounts. Refresh tokens rotate on use, and
/// a refresh token presented again after it rotated ends its grant, as the AT Protocol reference provider does: the
/// replay and the token that replaced it both stop working. No public host is touched.
/// </summary>
internal sealed class LoopbackPds : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, string> _handles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _signingKeys = new(StringComparer.Ordinal);
    private int _documentReads;
    private readonly Dictionary<string, Grant> _grants = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _issued;
    private int _refreshes;

    private LoopbackPds(WebApplication app) => _app = app;

    public string Origin { get; private set; } = "";

    /// <summary>How many refresh grants the token endpoint answered, successfully or not.</summary>
    public int Refreshes => Volatile.Read(ref _refreshes);

    /// <summary>How long the token endpoint takes to answer a refresh.</summary>
    public TimeSpan RefreshDelay { get; set; }

    /// <summary>When the token endpoint last received a refresh.</summary>
    public DateTimeOffset? RefreshedAt { get; private set; }

    /// <summary>The body length of <c>app.fixture.blob</c>, <c>app.fixture.stream</c>, <c>/fixture/blob</c> and <c>/fixture/stream</c>.</summary>
    public int BlobBytes { get; set; } = 16;

    /// <summary>Characters added to the authorization server's metadata document.</summary>
    public int MetadataPadding { get; set; }

    /// <summary>Completed when a revocation arrives; while <see cref="RevokeHold"/> is set, it is answered only once that completes.</summary>
    public TaskCompletionSource RevokeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? RevokeHold { get; set; }

    /// <summary>Whether the PDS drops the connection instead of answering XRPC.</summary>
    public bool DropXrpc { get; set; }

    public IFormCollection? Pushed { get; private set; }

    private const string RequestPrefix = "urn:fixture:";

    /// <summary>The state an authorization URL this provider handed out was pushed with.</summary>
    public static string StateOf(string authorization)
        => System.Web.HttpUtility.ParseQueryString(new Uri(authorization).Query)["request_uri"]![RequestPrefix.Length..];

    /// <summary>The callback query the provider sends after <paramref name="did"/> approves the request pushed with <paramref name="state"/>.</summary>
    public string Approval(string state, string did)
        => $"?state={Uri.EscapeDataString(state)}&iss={Uri.EscapeDataString(Origin)}&code={Uri.EscapeDataString("code:" + did)}";

    public static async Task<LoopbackPds> Start()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = builder.Build();
        var self = new LoopbackPds(app);
        app.MapPost("/par", async (HttpContext context) =>
        {
            var pushed = self.Pushed = await context.Request.ReadFormAsync();
            return Results.Json(new { request_uri = RequestPrefix + pushed["state"], expires_in = 60 }, statusCode: 201);
        });
        app.MapPost("/token", (Func<HttpContext, Task<IResult>>)self.Token);
        app.MapPost("/revoke", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            self.RevokeEntered.TrySetResult();
            if (self.RevokeHold is { } hold) await hold.Task.WaitAsync(context.RequestAborted);
            self.Revoke(form["token"].ToString());
            return Results.Ok();
        });
        app.MapGet("/xrpc/com.atproto.server.getSession", (HttpContext context) =>
        {
            if (self.DropXrpc) { context.Abort(); return Results.Empty; }
            return self.AccessOf(context) is { } did ? Results.Json(new { did }) : Results.Json(new { error = "InvalidToken" }, statusCode: 401);
        });
        app.MapGet("/xrpc/app.fixture.blob", (HttpContext context) =>
        {
            if (self.DropXrpc) { context.Abort(); return Results.Empty; }
            return self.AccessOf(context) is null
                ? Results.Json(new { error = "InvalidToken" }, statusCode: 401)
                : Results.Bytes(Enumerable.Repeat((byte)'a', self.BlobBytes).ToArray(), "application/octet-stream");
        });
        app.MapGet("/xrpc/app.fixture.stream", async (HttpContext context) =>
        {
            if (self.AccessOf(context) is null) { context.Response.StatusCode = 401; return; }
            await self.Stream(context);
        });
        app.MapPost("/xrpc/com.atproto.repo.putRecord", async (HttpContext context) =>
        {
            if (self.AccessOf(context) is not { } did) return Results.Json(new { error = "InvalidToken" }, statusCode: 401);
            var put = (await System.Text.Json.Nodes.JsonNode.ParseAsync(context.Request.Body))!.AsObject();
            if (put["repo"]!.GetValue<string>() != did) return Results.Json(new { error = "InvalidRequest" }, statusCode: 400);
            var address = $"at://{did}/{put["collection"]}/{put["rkey"]}";
            self.Records[address] = put["record"]!.ToJsonString();
            return Results.Json(new { uri = address, cid = self.Next("cid") });
        });
        app.MapPost("/xrpc/com.atproto.repo.deleteRecord", async (HttpContext context) =>
        {
            if (self.AccessOf(context) is not { } did) return Results.Json(new { error = "InvalidToken" }, statusCode: 401);
            var deleted = (await System.Text.Json.Nodes.JsonNode.ParseAsync(context.Request.Body))!.AsObject();
            self.Records.TryRemove($"at://{did}/{deleted["collection"]}/{deleted["rkey"]}", out _);
            return Results.Json(new { });
        });
        app.MapPost("/xrpc/com.atproto.repo.uploadBlob", async (HttpContext context) =>
        {
            if (self.AccessOf(context) is null) return Results.Json(new { error = "InvalidToken" }, statusCode: 401);
            using var bytes = new MemoryStream();
            await context.Request.Body.CopyToAsync(bytes);
            return Results.Json(new { blob = new Dictionary<string, object>
            {
                ["$type"] = "blob", ["ref"] = new Dictionary<string, string> { ["$link"] = self.Next("bafkrei") },
                ["mimeType"] = context.Request.ContentType ?? "", ["size"] = bytes.Length,
            } });
        });
        app.MapGet("/fixture/stream", self.Stream);
        app.MapGet("/fixture/blob", () => Results.Bytes(Enumerable.Repeat((byte)'b', self.BlobBytes).ToArray(), "application/octet-stream"));
        app.MapGet("/{**rest}", (string rest) => self.Document(rest));
        await app.StartAsync();
        self.Origin = app.Urls.Single();
        return self;
    }

    /// <summary>The records the PDS holds, by their <c>at://</c> address, each as its JSON.</summary>
    public ConcurrentDictionary<string, string> Records { get; } = new(StringComparer.Ordinal);

    /// <summary>An account this PLC directory resolves, with its PDS and authorization server here.</summary>
    public void Account(string did, string handle) => _handles[did] = handle;

    /// <summary>The verification method <paramref name="did"/>'s document carries, or none when null.</summary>
    public void SigningKey(string did, object? method)
    {
        if (method is null) _signingKeys.TryRemove(did, out _);
        else _signingKeys[did] = method;
    }

    /// <summary>How many DID documents this directory served.</summary>
    public int DocumentReads => Volatile.Read(ref _documentReads);

    /// <summary>A grant for <paramref name="did"/> whose current tokens are the ones given, as a sign-in left it.</summary>
    public void Seed(string did, string access, string refresh)
    {
        lock (_gate) _grants[did] = new Grant(did) { Access = access, Refresh = refresh };
    }

    /// <summary>Ends <paramref name="did"/>'s grant at the provider, as a person revoking it there does.</summary>
    public void End(string did)
    {
        lock (_gate) if (_grants.TryGetValue(did, out var grant)) grant.Ended = true;
    }

    /// <summary>The refresh token the provider would accept for <paramref name="did"/> now, or null when its grant ended.</summary>
    public string? CurrentRefresh(string did)
    {
        lock (_gate) return _grants.TryGetValue(did, out var grant) && !grant.Ended ? grant.Refresh : null;
    }

    private async Task<IResult> Token(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync();
        if (!context.Request.Headers.ContainsKey("DPoP")) return Results.Json(new { error = "invalid_dpop_proof" }, statusCode: 400);
        if (form["grant_type"] == "authorization_code")
        {
            var did = form["code"].ToString().Split(':', 2) is ["code", var subject] ? subject : "";
            if (!_handles.ContainsKey(did)) return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            Grant grant;
            var scope = Pushed?["scope"].ToString() is { Length: > 0 } pushed ? pushed : "atproto";
            lock (_gate) _grants[did] = grant = new Grant(did) { Access = Next("access"), Refresh = Next("refresh"), Scope = scope };
            return Issued(grant);
        }

        Interlocked.Increment(ref _refreshes);
        RefreshedAt = DateTimeOffset.UtcNow;
        if (RefreshDelay > TimeSpan.Zero) await Task.Delay(RefreshDelay);
        var presented = form["refresh_token"].ToString();
        lock (_gate)
        {
            foreach (var grant in _grants.Values)
            {
                if (grant.Ended) continue;
                if (grant.Refresh == presented)
                {
                    grant.Used.Add(presented);
                    grant.Access = Next("access");
                    grant.Refresh = Next("refresh");
                    return Issued(grant);
                }

                if (grant.Used.Contains(presented))
                {
                    grant.Ended = true;
                    return Results.Json(new { error = "invalid_grant", error_description = "refresh token replayed" }, statusCode: 400);
                }
            }
        }

        return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
    }

    // BlobBytes in chunks, with no declared length.
    private async Task Stream(HttpContext context)
    {
        context.Response.ContentType = "application/octet-stream";
        var chunk = Enumerable.Repeat((byte)'s', 64 * 1024).ToArray();
        for (var left = BlobBytes; left > 0; left -= chunk.Length)
            await context.Response.Body.WriteAsync(chunk.AsMemory(0, Math.Min(left, chunk.Length)), context.RequestAborted);
    }

    private void Revoke(string token)
    {
        lock (_gate)
            foreach (var grant in _grants.Values)
                if (grant.Refresh == token || grant.Used.Contains(token)) grant.Ended = true;
    }

    private string? AccessOf(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("DPoP ", StringComparison.Ordinal) || !context.Request.Headers.ContainsKey("DPoP")) return null;
        var token = authorization["DPoP ".Length..];
        lock (_gate) return _grants.Values.FirstOrDefault(grant => !grant.Ended && grant.Access == token)?.Did;
    }

    private IResult Issued(Grant grant) => Results.Json(new
    {
        access_token = grant.Access, token_type = "DPoP", expires_in = 3600, refresh_token = grant.Refresh, scope = grant.Scope, sub = grant.Did,
    });

    private string Next(string kind) => $"{kind}-{Interlocked.Increment(ref _issued)}";

    private IResult Document(string rest)
    {
        if (_handles.TryGetValue(rest, out var handle))
        {
            Interlocked.Increment(ref _documentReads);
            return Results.Json(new
            {
                id = rest,
                alsoKnownAs = new[] { "at://" + handle },
                verificationMethod = _signingKeys.TryGetValue(rest, out var method) ? new[] { method } : [],
                service = new[] { new { id = "#atproto_pds", type = "AtprotoPersonalDataServer", serviceEndpoint = Origin } },
            });
        }

        return rest switch
        {
            ".well-known/oauth-protected-resource" => Results.Json(new { resource = Origin, authorization_servers = new[] { Origin } }),
            ".well-known/oauth-authorization-server" => Results.Json(new
            {
                issuer = Origin, authorization_endpoint = Origin + "/authorize", token_endpoint = Origin + "/token",
                pushed_authorization_request_endpoint = Origin + "/par", revocation_endpoint = Origin + "/revoke",
                response_types_supported = new[] { "code" }, code_challenge_methods_supported = new[] { "S256" },
                dpop_signing_alg_values_supported = new[] { "ES256" }, padding = new string('p', MetadataPadding),
            }),
            _ => Results.NotFound(),
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class Grant(string did)
    {
        public string Did { get; } = did;
        public string Access { get; set; } = "";
        public string Refresh { get; set; } = "";
        public string Scope { get; set; } = "atproto";
        public HashSet<string> Used { get; } = new(StringComparer.Ordinal);
        public bool Ended { get; set; }
    }
}
