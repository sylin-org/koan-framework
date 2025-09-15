using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Web.Infrastructure;
using Koan.Web.Options;
using System.Diagnostics;

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
            var opts = options.Value;
            var pipeline = pipelineOptions.Value ?? new WebPipelineOptions();
            const string appliedKey = "Koan.Web.Applied";
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;
                // Greenfield boot: set AppHost, initialize env, run runtime
                Koan.Core.Hosting.App.AppHost.Current = app.ApplicationServices;
                try { Koan.Core.KoanEnv.TryInitialize(app.ApplicationServices); } catch { }
                var rt = app.ApplicationServices.GetService(typeof(Koan.Core.Hosting.Runtime.IAppRuntime)) as Koan.Core.Hosting.Runtime.IAppRuntime;
                rt?.Discover();
                rt?.Start();

                // Dev-only: reserve space for future diagnostics (SoC: data modules own their config and reporting)
                if (pipeline.UseExceptionHandler)
                {
                    app.UseExceptionHandler();
                }
                if (opts.EnableStaticFiles)
                {
                    app.UseDefaultFiles();
                    app.UseStaticFiles();
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
                // Lightweight health alias: if configured, respond to GET {HealthPath} with { status: "ok" }
                if (!string.IsNullOrWhiteSpace(opts.HealthPath))
                {
                    app.Use(async (ctx, next) =>
                    {
                        if (HttpMethods.IsGet(ctx.Request.Method)
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
                    app.UseRouting();
                    // Ensure auth middleware is in the correct position (after routing, before endpoints)
                    // Safe even if no authentication schemes are registered.
                    try
                    {
                        app.UseAuthentication();
                    }
                    catch (InvalidOperationException)
                    {
                        // No authentication services registered; ignore.
                    }
                    try
                    {
                        app.UseAuthorization();
                    }
                    catch (InvalidOperationException)
                    {
                        // No authorization services registered; ignore.
                    }
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
                    app.UseEndpoints(endpoints =>
                    {
                        // MVC controllers handle all endpoints including health
                        endpoints.MapControllers();
                    });
                }
                if (pipeline.UseRateLimiter)
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
            }
            next(app);
        };
    }
}

// WebPipelineOptions now lives in root namespace file `WebPipelineOptions.cs`
