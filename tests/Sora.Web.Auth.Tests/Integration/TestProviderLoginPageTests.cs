using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sora.Web.Auth.Tests.Integration;

public class TestProviderLoginPageTests
{
    [Fact]
    public async Task LoginHtml_IsReachable_And_HasTitle()
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(s =>
            {
                // Reference the TestProvider assembly so its routes are lit up
                var tp = Assembly.Load("Sora.Web.Auth.TestProvider");
                s.AddMvc().AddApplicationPart(tp);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapControllers());
            });

        using var host = await new TestServer(builder).Host.StartAsync();
        var client = host.GetTestClient();
        var resp = await client.GetAsync("/.testoauth/login.html");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.StartsWith("text/html", resp.Content.Headers.ContentType?.MediaType);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Sora TestProvider — Sign in", html);
    }
}
