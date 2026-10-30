using System.Text.Json;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Storage;
using Koan.Core;
using Koan.Web.Auth.Connector.Atproto.Options;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>Explicit, disposable-account-only proof over the sample's own store; stop the sample before running it.</summary>
public sealed class LocalProviderLifecycle
{
    public static bool Enabled => Environment.GetEnvironmentVariable("KOAN_ATPROTO_LIFECYCLE") is "disconnect" or "reauthorized";

    [Fact(Skip = "Requires an explicitly selected disposable local-provider lifecycle run.", SkipUnless = nameof(Enabled))]
    public async Task Real_refresh_and_checked_revocation()
    {
        var mode = Environment.GetEnvironmentVariable("KOAN_ATPROTO_LIFECYCLE")!;
        var sample = Path.GetFullPath(Environment.GetEnvironmentVariable("KOAN_ATPROTO_SAMPLE") ?? throw new InvalidOperationException("Set KOAN_ATPROTO_SAMPLE."));
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("KOAN_ATPROTO_FIXTURES") ?? throw new InvalidOperationException("Set KOAN_ATPROTO_FIXTURES.")));
        var account = fixture.RootElement.GetProperty("accounts").EnumerateArray().Single(x => x.GetProperty("role").GetString() == "owner");
        var did = account.GetProperty("did").GetString()!;
        var pds = new Uri(account.GetProperty("pds").GetString()!);
        Assert.True(pds.IsLoopback && pds.Scheme == "http", "This destructive lifecycle proof accepts only disposable loopback provider accounts.");

        // The sample's own composition over its content root: its store, its content-root discriminator and OS key ring.
        // No copied or exported keys.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = "AtprotoIdentity", ContentRootPath = sample, EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddKoan();
        await using var app = builder.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var environment = app.Services.GetRequiredService<IHostEnvironment>();
        var configured = builder.Configuration.GetSection("Koan:Web:Auth:Atproto").Get<AtprotoOptions>()!;
        foreach (var item in fixture.RootElement.GetProperty("accounts").EnumerateArray())
            configured.DevelopmentHandles[item.GetProperty("handle").GetString()!] = item.GetProperty("did").GetString()!;
        var auth = builder.Configuration.GetSection("Koan:Web:Auth").Get<AuthOptions>()!;
        var options = Microsoft.Extensions.Options.Options.Create(configured);
        var store = new AtprotoSessionStore(app.Services.GetRequiredService<IDataProtectionProvider>());
        using var network = new AtprotoHttp(options, environment);
        var sessions = new AtprotoSessions(store, network, options, Microsoft.Extensions.Options.Options.Create(auth), new Catalog(), environment, new KoanWebBaseUrl(null));
        var previous = await store.Read(did, TestContext.Current.CancellationToken);
        Assert.NotNull(previous?.Grant);
        var oldAccess = previous.Grant.TokenSet.AccessToken;
        var oldRefresh = previous.Grant.TokenSet.RefreshToken;
        // Expire local metadata only; the provider receives the original real refresh token and SDK DPoP.
        previous.Grant.TokenSet.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.NotNull(await store.Commit(did, previous.Grant, previous, TestContext.Current.CancellationToken));
        using var response = await sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, "The authenticated post-refresh PDS request failed.");
        using var identity = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(did, identity.RootElement.GetProperty("did").GetString());
        var refreshed = (await store.Read(did, TestContext.Current.CancellationToken))?.Grant;
        Assert.NotNull(refreshed);
        Assert.True(oldAccess != refreshed.TokenSet.AccessToken, "Real access-token rotation did not occur.");
        Assert.True(oldRefresh != refreshed.TokenSet.RefreshToken, "Real refresh-token rotation did not occur.");
        Assert.True(refreshed.TokenSet.ExpiresAt > DateTimeOffset.UtcNow);

        var revokedRefreshDenied = false;
        var localSessionRemoved = false;
        if (mode == "disconnect")
        {
            // Keep the last valid grant only in memory, so revocation is checked beyond local deletion.
            var retained = JsonSerializer.Deserialize<OAuthSessionData>(JsonSerializer.Serialize(refreshed))!;
            await sessions.Disconnect(did, TestContext.Current.CancellationToken);
            localSessionRemoved = (await store.Read(did, TestContext.Current.CancellationToken))?.Grant is null;
            Assert.True(localSessionRemoved);
            retained.TokenSet.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var memory = new MemoryOAuthSessionStore();
            await memory.StoreAsync(did, retained, TestContext.Current.CancellationToken);
            using var provider = new DPoPTokenProvider(network.Client, memory, clientId: retained.ClientId,
                redirectUri: retained.RedirectUri, scope: retained.Scope);
            Assert.True(await provider.RestoreSessionAsync(did, TestContext.Current.CancellationToken));
            try { await provider.GetAccessTokenAsync(TestContext.Current.CancellationToken); }
            catch (TokenRefreshException error) when (error.InnerException is OAuthException oauth && oauth.ErrorCode == "invalid_grant")
            { revokedRefreshDenied = true; }
            Assert.True(revokedRefreshDenied, "The provider did not reject the revoked refresh grant as invalid_grant.");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sessions.Send(did, "com.atproto.server.getSession", HttpMethod.Get, 64 * 1024, ct: TestContext.Current.CancellationToken));
        }

        var receipt = new
        {
            date = DateTimeOffset.UtcNow, mode, did, scope = "atproto", actualProviderRefresh = true,
            accessTokenRotated = true, refreshTokenRotated = true, authenticatedPdsRequest = (int)response.StatusCode,
            localExpiryMetadataAdjusted = true, checkedDisconnect = mode == "disconnect", localSessionRemoved,
            revokedRefreshDenied, revokedRefreshError = revokedRefreshDenied ? "invalid_grant" : null,
            secretsWritten = false
        };
        var output = Environment.GetEnvironmentVariable("KOAN_ATPROTO_RECEIPT") ?? throw new InvalidOperationException("Set KOAN_ATPROTO_RECEIPT.");
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            TestContext.Current.CancellationToken);
        await app.StopAsync(CancellationToken.None);
    }

    private sealed class Catalog : IAuthProviderCatalog
    {
        public IReadOnlyList<AuthProviderInfo> Providers { get; } = [new("atproto", "AT Protocol", "atproto", "enabled", true, true, false, 200, "Local proof", null, null, ["atproto"], "/auth/atproto/challenge")];
        public AuthProviderInfo? Default => Providers[0];
        public AuthProviderInfo? Find(string? id) => id == "atproto" ? Default : null;
    }
}
