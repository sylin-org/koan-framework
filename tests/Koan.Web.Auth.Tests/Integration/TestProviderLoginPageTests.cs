using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using Xunit;
using Koan.Web.Auth.Connector.Test.Extensions;

namespace Koan.Web.Auth.Tests.Integration;

public class TestProviderLoginPageTests
{
    [Fact]
    public async Task LoginHtml_IsReachable_And_HasTitle()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Development";
        builder.WebHost.UseTestServer();

        // Reference the TestProvider assembly so its routes are lit up
        var tp = Assembly.Load("Koan.Web.Auth.Connector.Test");
        builder.Services.AddMvc().AddApplicationPart(tp);

        var app = builder.Build();
        app.UseRouting();
        app.MapKoanTestProviderEndpoints();

        await app.StartAsync();

        var client = app.GetTestClient();
        var resp = await client.GetAsync("/.testoauth/login.html");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.StartsWith("text/html", resp.Content.Headers.ContentType?.MediaType);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Koan TestProvider - Sign in", html);

        await app.DisposeAsync();
    }
}

