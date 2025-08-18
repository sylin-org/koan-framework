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

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSoraWeb(this IApplicationBuilder app)
    {
        return app;
    }
}

// WebPipelineOptions now lives in root namespace file `WebPipelineOptions.cs`
