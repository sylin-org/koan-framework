using System.Security.Claims;
using Koan.Web.Auth.Connector.Atproto.Options;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// One application's store, a loopback provider with <c>accounts</c> accounts, and any number of nodes: each node is one
/// process of the application, with its own session store, transport and client cache, over the one store and key ring.
/// </summary>
internal sealed class SessionWorld : IAsyncDisposable
{
    public const string ClientId = "https://app.example/auth/atproto/client-metadata.json";

    private readonly List<Node> _nodes = [];

    private SessionWorld(TestStore store, LoopbackPds pds, string[] dids) { Store = store; Pds = pds; Dids = dids; }

    public TestStore Store { get; }
    public LoopbackPds Pds { get; }
    public string[] Dids { get; }

    public static string Did(int number) => "did:plc:" + number.ToString().PadLeft(24, 'a');

    public static string HandleOf(string did) => did["did:plc:".Length..] + ".test";

    public static async Task<SessionWorld> Start(int accounts, string adapter = "sqlite", Action? declare = null)
    {
        var store = await TestStore.Start(adapter, declare);
        var pds = await LoopbackPds.Start();
        var dids = Enumerable.Range(1, accounts).Select(Did).ToArray();
        foreach (var did in dids) pds.Account(did, HandleOf(did));
        return new SessionWorld(store, pds, dids);
    }

    /// <summary>
    /// A process of the application; <paramref name="claimLifetime"/> and <paramref name="claimRenewal"/> time its claims,
    /// and <paramref name="log"/> hears its session store.
    /// </summary>
    public Node Node(Action<AtprotoOptions>? configure = null, TimeSpan? claimLifetime = null, TimeSpan? claimRenewal = null,
        ILogger<AtprotoSessionStore>? log = null)
    {
        var options = new AtprotoOptions { DevelopmentAllowedOrigins = [Pds.Origin], DevelopmentPlcDirectory = Pds.Origin };
        foreach (var did in Dids) options.DevelopmentHandles[HandleOf(did)] = did;
        configure?.Invoke(options);
        var settings = Microsoft.Extensions.Options.Options.Create(options);
        var environment = new TestEnvironment();
        var auth = new AuthOptions();
        auth.Providers["atproto"] = new ProviderOptions { ClientId = ClientId };
        var store = new AtprotoSessionStore(Store.Protection(), log ?? NullLogger<AtprotoSessionStore>.Instance, claimLifetime, claimRenewal);
        var network = new AtprotoHttp(settings, environment);
        var node = new Node(store, network, new AtprotoSessions(store, network, settings,
            Microsoft.Extensions.Options.Options.Create(auth), new TestCatalog(), environment, new KoanWebBaseUrl(null)));
        _nodes.Add(node);
        return node;
    }

    /// <summary>A server-first sign-in of <paramref name="did"/> through <paramref name="node"/>: its challenge, and the callback after the provider approves.</summary>
    public async Task<ClaimsPrincipal> SignIn(Node node, string did)
    {
        var authorization = await node.Sessions.StartServer(Pds.Origin, new AuthenticationProperties(), "browser", TestContext.Current.CancellationToken);
        return await node.Sessions.Complete(Pds.Approval(LoopbackPds.StateOf(authorization), did), TestContext.Current.CancellationToken);
    }

    /// <summary>Makes <paramref name="did"/>'s stored access token expired, as time passing does, keeping everything else.</summary>
    public async Task ExpireAccess(Node node, string did)
    {
        var read = await node.Store.Read(did, TestContext.Current.CancellationToken);
        read!.Grant!.TokenSet.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.NotNull(await node.Store.Commit(did, read.Grant, read, TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var node in _nodes) node.Network.Dispose();
        await Pds.DisposeAsync();
        await Store.DisposeAsync();
    }
}

internal sealed record Node(AtprotoSessionStore Store, AtprotoHttp Network, AtprotoSessions Sessions);

internal sealed class TestEnvironment(string name = "Development") : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "AtprotoTests";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class TestCatalog : IAuthProviderCatalog
{
    public IReadOnlyList<AuthProviderInfo> Providers { get; } =
        [new("atproto", "AT Protocol", "atproto", "enabled", true, true, false, 200, "Specs", null, null, ["atproto"], "/auth/atproto/challenge")];
    public AuthProviderInfo? Default => Providers[0];
    public AuthProviderInfo? Find(string? id) => id == "atproto" ? Default : null;
}
