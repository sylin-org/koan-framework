using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Core;
using Koan.Data.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Tests.Cache.Web.Specs;

/// <summary>
/// The composed contract: a `Koan.Web` reference mounts the cache-control middleware itself, and
/// `KoanEnv.Gate` decides whether it honours anything. The application writes no pipeline call, so these
/// specs build a host whose own `Configure` adds nothing but the observing terminal.
/// <para>
/// <see cref="KoanCacheControlMiddlewareSpec"/> covers the header-to-behaviour mapping in isolation;
/// this spec covers whether the middleware is there at all.
/// </para>
/// </summary>
public sealed class KoanCacheControlTestServerSpec
{
    private static async Task<IHost> StartHost(string environment, bool? consent = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = environment,
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning"
        };
        if (consent is not null)
        {
            settings["Koan:Web:CacheControl:HonorClientHeaders"] = consent.Value ? "true" : "false";
        }

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment(environment);
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
                web.ConfigureServices(services => services.AddKoan());

                // The application contributes no cache-control middleware of its own.
                web.Configure(app => app.Run(async ctx =>
                {
                    var behavior = EntityContext.Current?.CacheBehavior;
                    ctx.Response.Headers["X-Observed-Behavior"] = behavior?.ToString() ?? "default";
                    await ctx.Response.WriteAsync("ok");
                }));
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task<string> ObserveBehavior(IHost host, string headerName, string headerValue)
    {
        using var client = host.GetTestServer().CreateClient();
        client.BaseAddress = new Uri("http://localhost");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation(headerName, headerValue);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return response.Headers.GetValues("X-Observed-Behavior").Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task Development_honours_client_cache_headers_without_an_application_pipeline_call()
    {
        using var host = await StartHost("Development");

        var observed = await ObserveBehavior(host, "Cache-Control", "no-cache");

        observed.Should().Be(CacheBehavior.Refresh.ToString());
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Development_maps_no_store_to_Bypass()
    {
        using var host = await StartHost("Development");

        var observed = await ObserveBehavior(host, "Cache-Control", "no-store");

        observed.Should().Be(CacheBehavior.Bypass.ToString());
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Production_ignores_client_cache_headers_until_an_operator_consents()
    {
        using var host = await StartHost("Production");

        var observed = await ObserveBehavior(host, "Cache-Control", "no-store");

        observed.Should().Be("default");
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Production_honours_client_cache_headers_once_consent_is_configured()
    {
        using var host = await StartHost("Production", consent: true);

        var observed = await ObserveBehavior(host, "Cache-Control", "no-store");

        observed.Should().Be(CacheBehavior.Bypass.ToString());
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_Koan_header_overrides_Cache_Control()
    {
        using var host = await StartHost("Development");

        var observed = await ObserveBehavior(host, "X-Koan-Cache", "readonly");

        observed.Should().Be(CacheBehavior.ReadOnly.ToString());
        await host.StopAsync(TestContext.Current.CancellationToken);
    }
}
