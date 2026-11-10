using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Web.Infrastructure;
using Koan.Web.Middleware;
using Koan.Web.Options;
using System.Diagnostics;
using System.Linq;

namespace Koan.Web.Hosting;

/// <summary>
/// Startup filter that wires Koan's default web pipeline, health endpoints, secure headers, and controller mapping.
/// Also logs dev-only data configuration with credential masking.
/// </summary>
internal sealed class KoanWebStartupFilter(IOptions<KoanWebOptions> options, IOptions<WebPipelineOptions> pipelineOptions) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Pipeline construction needs the web provider ambiently, but must not replace a newer
            // process owner. The flow scope also covers downstream startup filters and restores on exit.
            using var hostScope = Koan.Core.Hosting.App.AppHost.PushScope(app.ApplicationServices);

            var opts = options.Value;
            var pipeline = pipelineOptions.Value ?? new WebPipelineOptions();
            const string appliedKey = "Koan.Web.Applied";
            var mapsEndpoints = false;
            KeyValuePair<string, object?> routes = default;
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;
                // Initialize env and run the runtime against the scoped web provider. Every gate reads this snapshot, so
                // a failure here stops the boot rather than leaving the posture unknown.
                Koan.Core.KoanEnv.Initialize(
                    app.ApplicationServices.GetService<IConfiguration>(),
                    app.ApplicationServices.GetService<IHostEnvironment>());
                var rt = app.ApplicationServices.GetService(typeof(Koan.Core.Hosting.Runtime.IAppRuntime)) as Koan.Core.Hosting.Runtime.IAppRuntime;
                rt?.Discover();
                rt?.Start();

                // WEB-0078: the proxy and the base URL are decided before anything answers. Forwarded headers run first,
                // because they rewrite the scheme, host and client address everything after them reads; WEB-0081: the
                // origin each request arrived on is noted right after, as the proxy posture resolved it, before the
                // path base turns anything away; the path base runs next, so static files, the health alias, every
                // stage and every endpoint live under it, and a request outside it answers 404 here.
                var baseUrl = app.ApplicationServices.GetRequiredService<KoanWebBaseUrl>();
                if (KoanWebTrustedProxies.ForwardedHeaders(opts.TrustedProxies) is { } forwarded)
                {
                    app.UseForwardedHeaders(forwarded);
                }
                var origins = app.ApplicationServices.GetRequiredService<KoanWebOrigins>();
                app.Use((ctx, next) =>
                {
                    // ARCH-0144: a request the pipeline made in process arrived on no origin.
                    if (ctx.Features.Get<InProcessMark>() is null) origins.Note(ctx.Request);
                    return next(ctx);
                });
                if (baseUrl.PathBase.HasValue)
                {
                    var pathBase = baseUrl.PathBase;
                    app.UsePathBase(pathBase);
                    app.Use((ctx, next) =>
                    {
                        if (ctx.Request.PathBase.StartsWithSegments(pathBase)) return next(ctx);
                        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                        return Task.CompletedTask;
                    });
                }

                // Dev-only: reserve space for future diagnostics (SoC: data modules own their config and reporting)
                if (pipeline.UseExceptionHandler)
                {
                    app.UseExceptionHandler();
                }
                if (opts.EnableSecureHeaders && !opts.IsProxiedApi)
                {
                    app.Use((ctx, next) =>
                    {
                        // Ensure headers are normalized just before sending
                        ctx.Response.OnStarting(() =>
                        {
                            var headers = ctx.Response.Headers;
                            if (headers.ContainsKey(KoanWebConstants.Headers.XXssProtection))
                                headers.Remove(KoanWebConstants.Headers.XXssProtection);
                            if (!headers.ContainsKey(KoanWebConstants.Headers.XContentTypeOptions))
                                headers[KoanWebConstants.Headers.XContentTypeOptions] = KoanWebConstants.Policies.NoSniff;
                            if (!headers.ContainsKey(KoanWebConstants.Headers.XFrameOptions))
                                headers[KoanWebConstants.Headers.XFrameOptions] = KoanWebConstants.Policies.Deny;
                            if (!headers.ContainsKey(KoanWebConstants.Headers.ReferrerPolicy))
                                headers[KoanWebConstants.Headers.ReferrerPolicy] = KoanWebConstants.Policies.NoReferrer;
                            if (!string.IsNullOrWhiteSpace(opts.ContentSecurityPolicy) && !headers.ContainsKey(KoanWebConstants.Headers.ContentSecurityPolicy))
                                headers[KoanWebConstants.Headers.ContentSecurityPolicy] = opts.ContentSecurityPolicy;
                            return Task.CompletedTask;
                        });
                        return next();
                    });
                }
                // SEC-0039: one check for every endpoint, MCP's and a tool call's own request included, before anything can answer.
                if (opts.RefuseCrossSiteWrites)
                {
                    app.Use((ctx, next) => CrossSiteWrites.Refused(ctx.Request) ? CrossSiteWrites.Refuse(ctx) : next(ctx));
                }
                // Static/default files can terminate the pipeline, so register response headers first.
                var webEnvironment = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
                if (opts.EnableStaticFiles && webEnvironment.WebRootFileProvider is not NullFileProvider)
                {
                    app.UseDefaultFiles();
                    app.UseStaticFiles();
                }
                // Lightweight health alias: if configured, respond to GET or HEAD {HealthPath} with { status: "ok" } (WEB-0091)
                if (!string.IsNullOrWhiteSpace(opts.HealthPath))
                {
                    app.Use(async (ctx, next) =>
                    {
                        if ((HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
                            && string.Equals(ctx.Request.Path.Value, opts.HealthPath, StringComparison.OrdinalIgnoreCase))
                        {
                            ctx.Response.Headers[KoanWebConstants.Headers.XContentTypeOptions] = KoanWebConstants.Policies.NoSniff;
                            await ctx.Response.WriteAsJsonAsync(new { status = "ok" });
                            return;
                        }
                        await next();
                    });
                }
                if (opts.AutoMapControllers)
                {
                    // WEB-0088: a form asks for the method it cannot send, before routing picks the endpoint by it.
                    if (FormMethod.Served(app.ApplicationServices)) app.Use(new FormMethod().Override);
                    RunStage(app, KoanWebPipelineStage.BeforeRouting);
                    app.UseRouting();
                    // A default policy the application added answers before authorization, or a browser reads a 401 as a CORS failure.
                    app.UseCors();
                    // Ensure auth middleware is in the correct position (after routing, before endpoints)
                    // Safe even if no authentication schemes are registered. AddControllers registers the services both
                    // need, so a host without them stops here with ASP.NET's correction naming AddAuthorization.
                    app.UseAuthentication();
                    // Preserve AuthenticateResult.Fail separately from NoResult. ASP.NET leaves both callers
                    // anonymous, but a rejected credential must not inherit an Entity's anonymous authority.
                    app.UseMiddleware<EntityAuthenticationResultMiddleware>();
                    // WEB-0069: modules contribute middleware between authentication and authorization here
                    // (e.g. the zero-config dev identity), via the supported stage seam — no startup-filter ordering.
                    RunStage(app, KoanWebPipelineStage.AfterAuthentication);
                    app.UseAuthorization();
                    RunStage(app, KoanWebPipelineStage.AfterAuthorization);
                    // WEB-0075: the limiter runs after routing (so endpoint [EnableRateLimiting] metadata applies) and
                    // before the endpoints, which terminate the pipeline: placed after them it saw only unmatched requests.
                    if (pipeline.UseRateLimiter) UseRateLimiterIfRegistered(app);
                    // Add minimal tracing header for correlation (always safe)
                    app.Use(async (ctx, next) =>
                    {
                        var traceId = Activity.Current?.TraceId.ToString();
                        if (!string.IsNullOrEmpty(traceId))
                        {
                            ctx.Response.Headers[KoanWebConstants.Headers.KoanTraceId] = traceId;
                        }
                        await next();
                    });
                    mapsEndpoints = true;
                    routes = app.Properties.First(static property => property.Value is IEndpointRouteBuilder);
                }
                else if (pipeline.UseRateLimiter)
                {
                    // The application maps its own endpoints after Koan's middleware, so the limiter precedes them.
                    UseRateLimiterIfRegistered(app);
                }
            }
            // ARCH-0142: what the application adds in Program runs here, after authorization and before the endpoint, because
            // an endpoint ends the pipeline: placed after this, it would run only for a request no route answers.
            next(app);
            if (!mapsEndpoints) return;
            // An application that called UseRouting left its own route builder here. Koan's endpoints belong to the routing Koan placed.
            app.Properties[routes.Key] = routes.Value;
            app.UseEndpoints(endpoints =>
            {
                // MVC controllers handle all endpoints including health
                endpoints.MapControllers();

                // WEB-0069: modules map their own endpoints via IKoanEndpointContributor (e.g. MCP),
                // replacing reflection into module assemblies.
                foreach (var contributor in app.ApplicationServices
                             .GetServices<IKoanEndpointContributor>()
                             .OrderBy(c => c.Order))
                {
                    contributor.Map(endpoints);
                }
            });
        };
    }

    private static void UseRateLimiterIfRegistered(IApplicationBuilder app)
    {
        try
        {
            app.UseRateLimiter();
        }
        catch (InvalidOperationException)
        {
            // No limiter registered via AddRateLimiter(...); treat as a no-op per ADR-0012
        }
    }

    // WEB-0069: run the middleware contributors registered for a given pipeline stage, ordered.
    private static void RunStage(IApplicationBuilder app, KoanWebPipelineStage stage)
    {
        foreach (var contributor in app.ApplicationServices
                     .GetServices<IKoanWebPipelineContributor>()
                     .Where(c => c.Stage == stage)
                     .OrderBy(c => c.Order))
        {
            contributor.Configure(app);
        }
    }
}

// WebPipelineOptions now lives in root namespace file `WebPipelineOptions.cs`
