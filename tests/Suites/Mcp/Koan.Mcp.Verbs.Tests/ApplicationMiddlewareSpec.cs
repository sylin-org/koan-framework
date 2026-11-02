using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core.Commands;
using Koan.Mcp.Hosting;
using Koan.Web.Attributes;
using Koan.Web.Context;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>ARCH-0142: middleware an application adds in its own <c>Program</c> runs for a route, after routing and authorization and before the endpoint,
/// on <c>WebApplication</c> and on a generic host alike, and a tool call goes through it as a request does. An application's page answers a browser's problem.</summary>
public sealed class ApplicationMiddlewareSpec
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Settings(string path) => new()
    {
        ["Koan:Environment"] = "Development",
        ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
        ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=False",
        ["Koan:Mcp:EnableStdioTransport"] = "false",
    };

    private static void Remove(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"koan-verbs-middleware-{Guid.CreateVersion7():n}.db");

    private static WebApplicationBuilder NewApplication(string path)
    {
        AppHost.Current = null;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(Settings(path));
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TenantHolder>();
        builder.Services.AddSingleton<IStartupFilter, TenantMiddleware>();
        builder.Services.AddAuthorization(options => options.AddPolicy(Forum.NobodyPasses, policy => policy.RequireAssertion(_ => false)));
        builder.Services.AddKoan();
        return builder;
    }

    private static RequestDelegate Stamping(RequestDelegate next) => async context =>
    {
        // Routing has run: the endpoint is known here, as ASP.NET's own order would have it.
        context.Response.Headers["X-App-Middleware"] = context.GetEndpoint()?.DisplayName is null ? "no endpoint" : "ran";
        await next(context);
    };

    [Fact]
    public async Task On_WebApplication_it_runs_for_a_matched_controller_and_a_mapped_route_and_authorization_stays_ahead_of_it()
    {
        var path = NewPath();
        try
        {
            await using var app = NewApplication(path).Build();
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
            closed.IsSuccessStatusCode.Should().BeFalse();
            closed.Headers.Contains("X-App-Middleware").Should().BeFalse("authorization runs before it, as the pipeline says");

            await app.StopAsync(Ct);
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task A_tool_call_goes_through_it_as_a_request_does()
    {
        var path = NewPath();
        try
        {
            await using var app = NewApplication(path).Build();
            var seen = 0;
            app.Use(next => context =>
            {
                if (context.Request.Path == "/fidelity/tenant") Interlocked.Increment(ref seen);
                return next(context);
            });
            await app.StartAsync(Ct);
            using var scope = app.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<McpServer>().CreateHandler();

            var result = await handler.CallTool(new McpRpcHandler.ToolsCallParams { Name = "tenant_get", Arguments = new JObject() }, Ct);

            result.IsError.Should().BeFalse(result.Content[0].Text);
            seen.Should().Be(1);
            await app.StopAsync(Ct);
        }
        finally
        {
            Remove(path);
        }
    }

    [Theory]
    [InlineData(Always.Name)]
    [InlineData("AuthenticationTypes.Federation")]
    public async Task On_WebApplication_a_principal_a_web_context_placed_is_the_one_the_middleware_and_the_action_read(string identityType)
    {
        var path = NewPath();
        try
        {
            Always.IdentityType = identityType;
            var builder = NewApplication(path);
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, Always>(Always.Name, _ => { });
            builder.Services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = options.DefaultScheme = Always.Name);
            builder.Services.AddSingleton<IWebContextContributor, ProjectsARole>();
            await using var app = builder.Build();
            app.Use(next => async context =>
            {
                context.Response.Headers["X-App-Sees-Role"] = context.User.IsInRole("projected").ToString();
                await next(context);
            });
            await app.StartAsync(Ct);
            using var client = app.GetTestClient();

            using var answer = await client.GetAsync("/middleware-seen/role", Ct);
            answer.StatusCode.Should().Be(HttpStatusCode.OK);
            (await answer.Content.ReadAsStringAsync(Ct)).Should().Be("true");
            answer.Headers.GetValues("X-App-Sees-Role").Should().Equal("True");
            using var gated = await client.GetAsync("/middleware-seen/needs-role", Ct);
            gated.StatusCode.Should().Be(HttpStatusCode.OK, "the policy reads the principal placed, whatever type its identity has");
            await app.StopAsync(Ct);
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task On_WebApplication_a_principal_narrowed_by_a_web_context_does_not_fall_back_to_the_wider_one()
    {
        var path = NewPath();
        try
        {
            Always.IdentityType = "AuthenticationTypes.Federation";
            var builder = NewApplication(path);
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, Always>(Always.Name, _ => { });
            builder.Services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = options.DefaultScheme = Always.Name);
            builder.Services.AddSingleton<IWebContextContributor, NarrowsIt>();
            await using var app = builder.Build();
            await app.StartAsync(Ct);
            using var client = app.GetTestClient();

            using var answer = await client.GetAsync("/middleware-seen/name", Ct);
            (await answer.Content.ReadAsStringAsync(Ct)).Should().Be("narrowed");
            await app.StopAsync(Ct);
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task A_default_cors_policy_answers_a_refusal_so_a_browser_reads_it_as_a_refusal()
    {
        var path = NewPath();
        try
        {
            var builder = NewApplication(path);
            builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("https://other.test").AllowAnyHeader().AllowAnyMethod()));
            await using var app = builder.Build();
            await app.StartAsync(Ct);
            using var client = app.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/middleware-seen/closed");
            request.Headers.Add("Origin", "https://other.test");
            using var refused = await client.SendAsync(request, Ct);
            refused.IsSuccessStatusCode.Should().BeFalse();
            refused.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();

            using var preflight = new HttpRequestMessage(HttpMethod.Options, "/middleware-seen/closed");
            preflight.Headers.Add("Origin", "https://other.test");
            preflight.Headers.Add("Access-Control-Request-Method", "GET");
            using var allowed = await client.SendAsync(preflight, Ct);
            allowed.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();
            await app.StopAsync(Ct);
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task With_no_cors_policy_a_cross_origin_request_is_answered_as_it_was()
    {
        var path = NewPath();
        try
        {
            await using var app = NewApplication(path).Build();
            await app.StartAsync(Ct);
            using var client = app.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/middleware-seen/open");
            request.Headers.Add("Origin", "https://other.test");
            using var answer = await client.SendAsync(request, Ct);
            answer.StatusCode.Should().Be(HttpStatusCode.OK);
            answer.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
            await app.StopAsync(Ct);
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task A_fallback_the_application_maps_answers_only_what_no_route_answers()
    {
        var path = NewPath();
        try
        {
            await using var app = NewApplication(path).Build();
            app.MapFallback(() => "fallback");
            await app.StartAsync(Ct);
            using var client = app.GetTestClient();

            (await client.GetStringAsync("/nothing-here", Ct)).Should().Be("fallback");
            using var route = await client.GetAsync("/middleware-seen/role", Ct);
            (await route.Content.ReadAsStringAsync(Ct)).Should().Be("false", "a controller answers before a fallback");
            await app.StopAsync(Ct);
        }
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task On_a_generic_host_it_runs_and_a_route_the_application_maps_itself_still_answers()
    {
        var path = NewPath();
        try
        {
            AppHost.Current = null;
            using var host = await Host.CreateDefaultBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.UseEnvironment("Development");
                    web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings(path)));
                    web.ConfigureServices(services =>
                    {
                        services.AddScoped<TenantHolder>();
                        services.AddAuthorization(options => options.AddPolicy(Forum.NobodyPasses, policy => policy.RequireAssertion(_ => false)));
                        services.AddKoan();
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
        finally
        {
            Remove(path);
        }
    }

    [Fact]
    public async Task An_applications_view_answers_a_browser_in_place_of_the_plain_page()
    {
        var path = NewPath();
        try
        {
            var builder = NewApplication(path);
            builder.Services.AddSingleton<IProblemView, OwnView>();
            await using var app = builder.Build();
            await app.StartAsync(Ct);
            using var client = app.GetTestClient();

            using var browser = new HttpRequestMessage(HttpMethod.Post, "/middleware-seen/conflict");
            browser.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
            using var page = await client.SendAsync(browser, Ct);
            page.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await page.Content.ReadAsStringAsync(Ct)).Should().Be("own: conflict: Already there.");

            using var json = await client.PostAsync("/middleware-seen/conflict", null, Ct);
            json.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            await app.StopAsync(Ct);
        }
        finally
        {
            Remove(path);
        }
    }

    private sealed class OwnView : IProblemView
    {
        public IActionResult Render(HttpContext http, ProblemDetails problem) => new ContentResult
        {
            StatusCode = problem.Status,
            ContentType = "text/html",
            Content = $"own: {problem.Extensions["kind"]}: {problem.Detail}",
        };
    }

    private sealed class Always(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "always";

        /// <summary>What the identity's type says, which need not be the scheme's name. This assembly's tests run one at a time.</summary>
        public static string IdentityType { get; set; } = Name;

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], IdentityType)), Name)));
    }

    private sealed class NarrowsIt : IWebContextContributor
    {
        public int Order => 0;

        public ValueTask ContributeAsync(WebContext context)
        {
            if (context.HttpContext.User.Identity?.IsAuthenticated == true)
                context.UsePrincipal(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "narrowed")], Always.IdentityType)));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProjectsARole : IWebContextContributor
    {
        public int Order => 0;

        public ValueTask ContributeAsync(WebContext context)
        {
            if (context.HttpContext.User.Identity?.IsAuthenticated == true)
                context.UsePrincipal(new ClaimsPrincipal(context.HttpContext.User.Identities.Append(new ClaimsIdentity([new Claim(ClaimTypes.Role, "projected")], Always.Name))));
            return ValueTask.CompletedTask;
        }
    }
}

[Route("/middleware-seen")]
public sealed class MiddlewareSeenController : ControllerBase
{
    [HttpGet("open")]
    public IActionResult Open() => Ok();

    [HttpGet("role")]
    public IActionResult Role() => Content(User.IsInRole("projected") ? "true" : "false");

    [HttpGet("needs-role")]
    [Authorize(Roles = "projected")]
    public IActionResult NeedsRole() => Ok();

    [HttpGet("name")]
    public IActionResult Who() => Content(User.Identity?.Name ?? "nobody");

    [HttpGet("closed")]
    [Authorize]
    public IActionResult Closed() => Ok();

    [HttpPost("conflict")]
    public IActionResult Conflicted() => this.Answer<string>(new Outcome<string>.Refused(Reason.Conflict("Already there.")), _ => Ok());
}
