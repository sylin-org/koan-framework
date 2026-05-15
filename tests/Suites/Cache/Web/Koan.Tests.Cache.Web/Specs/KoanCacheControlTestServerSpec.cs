using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Tests.Cache.Web.Specs;

/// <summary>
/// End-to-end ASP.NET Core integration test for <c>UseKoanCacheControl</c>. Unlike the
/// unit-level <c>KoanCacheControlMiddlewareSpec</c> which exercises the middleware in
/// isolation, this spec wires the middleware into a real <see cref="TestServer"/> request
/// pipeline and verifies the ambient <see cref="EntityContext.CacheBehavior"/> is visible
/// to a downstream endpoint handler the way it would be in production.
/// </summary>
public sealed class KoanCacheControlTestServerSpec
{
    private static async Task<TestServer> BuildServer()
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.Configure(app =>
                {
                    app.UseKoanCacheControl();
                    app.Run(async ctx =>
                    {
                        // Mirror the active CacheBehavior back to the client as a response
                        // header. If null (no override), report "default" explicitly.
                        var behavior = EntityContext.Current?.CacheBehavior;
                        ctx.Response.Headers["X-Observed-Behavior"] = behavior?.ToString() ?? "default";
                        await ctx.Response.WriteAsync("ok");
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        return host.GetTestServer();
    }

    [Fact]
    public async Task Cache_Control_no_cache_header_pushes_Refresh_into_request_scope()
    {
        using var server = await BuildServer();
        using var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Observed-Behavior").Should().ContainSingle()
            .Which.Should().Be(CacheBehavior.Refresh.ToString());
    }

    [Fact]
    public async Task Cache_Control_no_store_header_pushes_Bypass_into_request_scope()
    {
        using var server = await BuildServer();
        using var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-store");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Observed-Behavior").Should().ContainSingle()
            .Which.Should().Be(CacheBehavior.Bypass.ToString());
    }

    [Fact]
    public async Task X_Koan_Cache_header_overrides_Cache_Control()
    {
        using var server = await BuildServer();
        using var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("X-Koan-Cache", "bypass");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Observed-Behavior").Should().ContainSingle()
            .Which.Should().Be(CacheBehavior.Bypass.ToString());
    }

    [Fact]
    public async Task X_Koan_Cache_readonly_maps_to_ReadOnly_behavior()
    {
        using var server = await BuildServer();
        using var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("X-Koan-Cache", "readonly");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Observed-Behavior").Should().ContainSingle()
            .Which.Should().Be(CacheBehavior.ReadOnly.ToString());
    }

    [Fact]
    public async Task Request_with_no_cache_headers_yields_default_behavior()
    {
        using var server = await BuildServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Observed-Behavior").Should().ContainSingle()
            .Which.Should().Be("default");
    }

    [Fact]
    public async Task Scope_does_not_leak_across_concurrent_requests()
    {
        using var server = await BuildServer();
        using var client = server.CreateClient();

        var bypassRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        bypassRequest.Headers.TryAddWithoutValidation("X-Koan-Cache", "bypass");

        var defaultRequest = new HttpRequestMessage(HttpMethod.Get, "/");

        // Fire concurrently; AsyncLocal scoping must keep each request isolated.
        var bypassTask = client.SendAsync(bypassRequest);
        var defaultTask = client.SendAsync(defaultRequest);

        await Task.WhenAll(bypassTask, defaultTask);

        bypassTask.Result.Headers.GetValues("X-Observed-Behavior").Should().ContainSingle()
            .Which.Should().Be(CacheBehavior.Bypass.ToString());

        defaultTask.Result.Headers.GetValues("X-Observed-Behavior").Should().ContainSingle()
            .Which.Should().Be("default");
    }
}
