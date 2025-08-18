using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Core;
using System.Diagnostics;
using Sora.Web.Infrastructure;

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
            var pipeline = pipelineOptions.Value ?? new Sora.Web.WebPipelineOptions();
            const string appliedKey = "Sora.Web.Applied";
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;
                app.ApplicationServices.UseSora().StartSora();

                // Dev-only: log effective data adapter configuration (masked)
                try
                {
                    var env = app.ApplicationServices.GetService<IHostEnvironment>();
                    if (env?.IsDevelopment() == true)
                    {
                        var cfg = app.ApplicationServices.GetService<IConfiguration>();
                        var lf = app.ApplicationServices.GetService<ILoggerFactory>();
                        if (cfg is not null && lf is not null)
                        {
                            var log = lf.CreateLogger("Sora.Data.Config");
                            string Mask(string? s) => Redaction.DeIdentify(s);

                            var csDefault = cfg.GetConnectionString("Default");
                            if (!string.IsNullOrWhiteSpace(csDefault))
                                log.LogInformation("[Sora] Dev data config: ConnectionStrings:Default={Conn}", Mask(csDefault));

                            var mongo = cfg.GetSection("Sora:Data:Mongo");
                            var mongoDb = mongo["Database"]; var mongoCs = mongo["ConnectionString"] ?? csDefault;
                            if (!string.IsNullOrWhiteSpace(mongoDb) || !string.IsNullOrWhiteSpace(mongoCs))
                                log.LogInformation("[Sora] Dev data config: mongo Database={Db} Conn={Conn}", mongoDb ?? "(null)", Mask(mongoCs));

                            var sqlite = cfg.GetSection("Sora:Data:Sqlite");
                            var sqliteCs = sqlite["ConnectionString"] ?? csDefault;
                            if (!string.IsNullOrWhiteSpace(sqliteCs))
                                log.LogInformation("[Sora] Dev data config: sqlite Conn={Conn}", Mask(sqliteCs));

                            var json = cfg.GetSection("Sora:Data:Json");
                            var jsonDir = json["DirectoryPath"];
                            if (!string.IsNullOrWhiteSpace(jsonDir))
                                log.LogInformation("[Sora] Dev data config: json DirectoryPath={Dir}", jsonDir);

                            // Enumerate named sources if present
                            var sources = cfg.GetSection("Sora:Data:Sources");
                            foreach (var src in sources.GetChildren())
                            {
                                var name = src.Key;
                                foreach (var provider in src.GetChildren())
                                {
                                    var providerId = provider.Key;
                                    var pCs = provider["ConnectionString"];
                                    var pDb = provider["Database"];
                                    if (!string.IsNullOrWhiteSpace(pCs) || !string.IsNullOrWhiteSpace(pDb))
                                        log.LogInformation("[Sora] Dev data source: {Name}:{Provider} Conn={Conn} Db={Db}", name, providerId, Mask(pCs), pDb ?? "(null)");
                                }
                            }
                        }
                    }
                }
                catch { /* best-effort only */ }
                if (pipeline.UseExceptionHandler)
                {
                    app.UseExceptionHandler();
                }
                if (opts.EnableStaticFiles)
                {
                    app.UseDefaultFiles();
                    app.UseStaticFiles();
                }
                if (opts.EnableSecureHeaders)
                {
                    app.Use(async (ctx, next) =>
                    {
                        ctx.Response.Headers.TryAdd(SoraWebConstants.Headers.XContentTypeOptions, SoraWebConstants.Policies.NoSniff);
                        ctx.Response.Headers.TryAdd(SoraWebConstants.Headers.XFrameOptions, SoraWebConstants.Policies.Deny);
                        ctx.Response.Headers.TryAdd(SoraWebConstants.Headers.ReferrerPolicy, SoraWebConstants.Policies.NoReferrer);
                        if (!string.IsNullOrWhiteSpace(opts.ContentSecurityPolicy))
                            ctx.Response.Headers[SoraWebConstants.Headers.ContentSecurityPolicy] = opts.ContentSecurityPolicy;
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

                        // Optional lightweight health path (avoid conflict with controller default /api/health)
                        if (!string.IsNullOrWhiteSpace(opts.HealthPath) && !string.Equals(opts.HealthPath, SoraWebConstants.Routes.ApiHealth, StringComparison.OrdinalIgnoreCase))
                        {
                            endpoints.MapGet(opts.HealthPath, async context =>
                            {
                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsJsonAsync(new { status = "ok" });
                            });
                        }
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

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSoraWeb(this IApplicationBuilder app)
    {
        return app;
    }
}

// WebPipelineOptions now lives in root namespace file `WebPipelineOptions.cs`
