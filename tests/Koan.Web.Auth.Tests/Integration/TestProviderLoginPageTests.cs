using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;
using Koan.Web.Auth.TestProvider.Extensions;

namespace Koan.Web.Auth.Tests.Integration;

public class TestProviderLoginPageTests
{
    [Fact]
    public async Task LoginHtml_IsReachable_And_HasTitle()
    {
        var builder = new WebHostBuilder()
            .UseEnvironment("Development")
            .UseTestServer()
            .ConfigureServices(s =>
            {
                // Reference the TestProvider assembly so its routes are lit up
                var tp = Assembly.Load("Koan.Web.Auth.TestProvider");
                s.AddMvc().AddApplicationPart(tp);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapKoanTestProviderEndpoints());
            });

        using var server = new TestServer(builder);
        var client = server.CreateClient();
        var resp = await client.GetAsync("/.testoauth/login.html");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.StartsWith("text/html", resp.Content.Headers.ContentType?.MediaType);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Koan TestProvider - Sign in", html);
    }
}
