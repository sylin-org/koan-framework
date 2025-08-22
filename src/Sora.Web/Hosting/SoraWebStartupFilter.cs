using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Web.Infrastructure;
using System.Diagnostics;
using Sora.Web.Options;

namespace Sora.Web.Hosting;

/// <summary>
/// Startup filter that wires Sora's default web pipeline, health endpoints, secure headers, and controller mapping.
/// Also logs dev-only data configuration with credential masking.
/// </summary>
internal sealed class SoraWebStartupFilter(IOptions<SoraWebOptions> options, IOptions<WebPipelineOptions> pipelineOptions) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            var opts = options.Value;
            var pipeline = pipelineOptions.Value ?? new WebPipelineOptions();
            const string appliedKey = "Sora.Web.Applied";
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;
                app.ApplicationServices.UseSora().StartSora();

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
                            if (headers.ContainsKey(SoraWebConstants.Headers.XXssProtection))
                                headers.Remove(SoraWebConstants.Headers.XXssProtection);
                            if (!headers.ContainsKey(SoraWebConstants.Headers.XContentTypeOptions))
                                headers[SoraWebConstants.Headers.XContentTypeOptions] = SoraWebConstants.Policies.NoSniff;
                            if (!headers.ContainsKey(SoraWebConstants.Headers.XFrameOptions))
                                headers[SoraWebConstants.Headers.XFrameOptions] = SoraWebConstants.Policies.Deny;
                            if (!headers.ContainsKey(SoraWebConstants.Headers.ReferrerPolicy))
                                headers[SoraWebConstants.Headers.ReferrerPolicy] = SoraWebConstants.Policies.NoReferrer;
                            if (!string.IsNullOrWhiteSpace(opts.ContentSecurityPolicy) && !headers.ContainsKey(SoraWebConstants.Headers.ContentSecurityPolicy))
                                headers[SoraWebConstants.Headers.ContentSecurityPolicy] = opts.ContentSecurityPolicy;
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
                            ctx.Response.Headers[SoraWebConstants.Headers.XContentTypeOptions] = SoraWebConstants.Policies.NoSniff;
                            await ctx.Response.WriteAsJsonAsync(new { status = "ok" });
                            return;
                        }
                        await next();
                    });
                }
                if (opts.AutoMapControllers)
                {
                    app.UseRouting();
                    // Add minimal tracing header for correlation (always safe)
                    app.Use(async (ctx, next) =>
                    {
                        var traceId = Activity.Current?.TraceId.ToString();
                        if (!string.IsNullOrEmpty(traceId))
                        {
                            ctx.Response.Headers[SoraWebConstants.Headers.SoraTraceId] = traceId;
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
