using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit;
using Koan.Web.Auth.Connector.Test.Infrastructure;
using Koan.Web.Auth.Connector.Test.Options;
using Koan.Web.Auth.Connector.Test.Extensions;

namespace Koan.Web.Auth.Tests.Integration;

public class TestProviderAuthorizeSecurityTests
{
    private static HttpClient CreateClientWithUser(WebApplication app)
    {
        var client = app.GetTestClient();
        var cookieVal = Uri.EscapeDataString("dev|dev@example.com|");
        const string CookieUser = "_tp_user"; // mirror TestProvider.Constants.CookieUser
        client.DefaultRequestHeaders.Add("Cookie", $"{CookieUser}={cookieVal}");
        return client;
    }

    private static async Task<WebApplication> CreateServerAsync(Action<IConfigurationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Development";
        builder.WebHost.UseTestServer();

        var mem = new Dictionary<string, string?>
        {
            ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
            ["Koan:Web:Auth:TestProvider:ClientId"] = "test-client",
            ["Koan:Web:Auth:TestProvider:AllowedRedirectUris:0"] = "https://app.local/callback",
            ["Koan:Web:Auth:TestProvider:AllowedRedirectUris:1"] = "/signin-test",
        };
        builder.Configuration.AddInMemoryCollection(mem);
        configure?.Invoke(builder.Configuration as IConfigurationBuilder);

        var tp = Assembly.Load("Koan.Web.Auth.Connector.Test");
        builder.Services.AddMvc().AddApplicationPart(tp);
        builder.Services.AddSingleton<DevTokenStore>();
        builder.Services.AddOptions();
        builder.Services.Configure<TestProviderOptions>(builder.Configuration.GetSection(TestProviderOptions.SectionPath));

        var app = builder.Build();
        app.UseRouting();
        app.MapKoanTestProviderEndpoints();

        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Authorize_Rejects_InvalidRedirect_Format()
    {
        await using var app = await CreateServerAsync();
        var client = CreateClientWithUser(app);
        var url = "/.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=not-a-uri";
        var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("invalid_redirect_uri", text);
    }

    [Fact]
    public async Task Authorize_Rejects_Redirect_NotInWhitelist()
    {
        await using var app = await CreateServerAsync();
        var client = CreateClientWithUser(app);
        var url = "/.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=https://evil.local/callback";
        var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("unauthorized_redirect_uri", text);
    }

    [Fact]
    public async Task Authorize_Allows_Redirect_When_AbsoluteMatch()
    {
        await using var app = await CreateServerAsync();
        var client = CreateClientWithUser(app);
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
        await using var app = await CreateServerAsync();
        var client = CreateClientWithUser(app);
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
        await using var app = await CreateServerAsync();
        var client = CreateClientWithUser(app);
        var url = "/.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=https://app.local/callback&code_challenge=abc&code_challenge_method=plain";
        var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("unsupported_code_challenge_method", text);
    }
}

