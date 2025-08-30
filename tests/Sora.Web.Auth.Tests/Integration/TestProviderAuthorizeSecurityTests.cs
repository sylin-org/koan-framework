using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit;
using Sora.Web.Auth.TestProvider.Infrastructure;
using Sora.Web.Auth.TestProvider.Options;
using Sora.Web.Auth.TestProvider.Extensions;

namespace Sora.Web.Auth.Tests.Integration;

public class TestProviderAuthorizeSecurityTests
{
    private static HttpClient CreateClientWithUser(TestServer server)
    {
        var client = server.CreateClient();
        var cookieVal = Uri.EscapeDataString("dev|dev@example.com|");
        const string CookieUser = "_tp_user"; // mirror TestProvider.Constants.CookieUser
        client.DefaultRequestHeaders.Add("Cookie", $"{CookieUser}={cookieVal}");
        return client;
    }
    private static TestServer CreateServer(Action<IConfigurationBuilder>? configure = null)
    {
        var builder = new WebHostBuilder()
            .UseEnvironment("Development")
            .UseTestServer()
            .ConfigureAppConfiguration((_, cfg) =>
            {
                var mem = new Dictionary<string, string?>
                {
                    ["Sora:Web:Auth:TestProvider:Enabled"] = "true",
                    ["Sora:Web:Auth:TestProvider:ClientId"] = "test-client",
                    ["Sora:Web:Auth:TestProvider:AllowedRedirectUris:0"] = "https://app.local/callback",
                    ["Sora:Web:Auth:TestProvider:AllowedRedirectUris:1"] = "/signin-test",
                };
                cfg.AddInMemoryCollection(mem);
                configure?.Invoke(cfg);
            })
            .ConfigureServices((ctx, s) =>
            {
                var tp = Assembly.Load("Sora.Web.Auth.TestProvider");
                s.AddMvc().AddApplicationPart(tp);
                s.AddSingleton<DevTokenStore>();
                s.AddOptions();
                s.Configure<TestProviderOptions>(ctx.Configuration.GetSection(TestProviderOptions.SectionPath));
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapSoraTestProviderEndpoints());
            });

        return new TestServer(builder);
    }

    [Fact]
    public async Task Authorize_Rejects_InvalidRedirect_Format()
    {
        using var server = CreateServer();
    var client = CreateClientWithUser(server);
        var url = "/.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=not-a-uri";
        var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("invalid_redirect_uri", text);
    }

    [Fact]
    public async Task Authorize_Rejects_Redirect_NotInWhitelist()
    {
        using var server = CreateServer();
    var client = CreateClientWithUser(server);
        var url = "/.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=https://evil.local/callback";
        var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("unauthorized_redirect_uri", text);
    }

    [Fact]
    public async Task Authorize_Allows_Redirect_When_AbsoluteMatch()
    {
        using var server = CreateServer();
    var client = CreateClientWithUser(server);
        var url = "/.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=https://app.local/callback";
        var resp = await client.GetAsync(url);
        // Expect a redirect to the whitelisted URI with a code
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        Assert.StartsWith("https://app.local/callback", resp.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Contains("code=", resp.Headers.Location!.Query);
    }

    [Fact]
    public async Task Authorize_Allows_Redirect_When_PathMatch()
    {
        using var server = CreateServer();
    var client = CreateClientWithUser(server);
        var url = "/.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=https://any.host/signin-test";
        var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        Assert.EndsWith("/signin-test", resp.Headers.Location!.AbsolutePath);
        Assert.Contains("code=", resp.Headers.Location!.Query);
    }

    [Fact]
    public async Task Authorize_Rejects_Unsupported_PKCE_Method()
    {
        using var server = CreateServer();
    var client = CreateClientWithUser(server);
        var url = "/.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=https://app.local/callback&code_challenge=abc&code_challenge_method=plain";
        var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("unsupported_code_challenge_method", text);
    }
}
