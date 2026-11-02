using System.Net;
using AwesomeAssertions;
using Koan.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>ARCH-0142: middleware an application adds in its own <c>Program</c> runs for a route, after routing and authorization and before the endpoint,
/// on <c>WebApplication</c> and on a generic host alike.</summary>
[Collection(RestEntityCollection.Name)]
public sealed class ApplicationMiddlewareSpec
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Koan:Environment"] = "Development",
        ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
        ["Koan:BackgroundServices:Enabled"] = "false",
    };

    private static RequestDelegate Stamping(RequestDelegate next) => async context =>
    {
        // Routing has run: the endpoint is known here, as ASP.NET's own order would have it.
        context.Response.Headers["X-App-Middleware"] = context.GetEndpoint()?.DisplayName is null ? "no endpoint" : "ran";
        await next(context);
    };

    [Fact]
    public async Task On_WebApplication_it_runs_for_a_matched_controller_and_a_mapped_route_and_authorization_stays_ahead_of_it()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings);
        builder.Services.AddKoan();
        builder.Services.AddKoanControllersFrom<CogController>();
        await using var app = builder.Build();
        app.Use(Stamping);
        app.MapGet("/program-mapped", () => "mapped");
        await app.StartAsync(Ct);
        using var client = app.GetTestClient();

        using var controller = await client.GetAsync("/middleware-seen/open", Ct);
        controller.StatusCode.Should().Be(HttpStatusCode.OK);
        controller.Headers.GetValues("X-App-Middleware").Should().Equal("ran");

        using var mapped = await client.GetAsync("/program-mapped", Ct);
        mapped.StatusCode.Should().Be(HttpStatusCode.OK);
        mapped.Headers.Contains("X-App-Middleware").Should().BeTrue();

        using var closed = await client.GetAsync("/middleware-seen/closed", Ct);
        closed.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Found);
        closed.Headers.Contains("X-App-Middleware").Should().BeFalse("authorization runs before it, as the pipeline says");

        await app.StopAsync(Ct);
    }

    [Fact]
    public async Task On_a_generic_host_it_runs_and_a_route_the_application_maps_itself_still_answers()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment("Development");
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
                web.ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddKoanControllersFrom<CogController>();
                });
                web.Configure(app =>
                {
                    app.Use(Stamping);
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet("/own-mapped", () => "own"));
                });
            })
            .StartAsync(Ct);
        using var client = host.GetTestClient();

        using var controller = await client.GetAsync("/middleware-seen/open", Ct);
        controller.StatusCode.Should().Be(HttpStatusCode.OK);
        controller.Headers.GetValues("X-App-Middleware").Should().Equal("ran");

        using var mapped = await client.GetAsync("/own-mapped", Ct);
        mapped.StatusCode.Should().Be(HttpStatusCode.OK);

        await host.StopAsync(Ct);
    }
}

[Route("/middleware-seen")]
public sealed class MiddlewareSeenController : ControllerBase
{
    [HttpGet("open")]
    public IActionResult Open() => Ok();

    [HttpGet("closed")]
    [Authorize]
    public IActionResult Closed() => Ok();
}
