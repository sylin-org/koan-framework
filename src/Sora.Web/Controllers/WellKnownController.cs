using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core.Observability;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Web.Infrastructure;

namespace Sora.Web.Controllers;

[ApiController]
[Produces("application/json")]
[Route(".well-known/sora")]
public sealed class WellKnownController(
    IHostEnvironment env,
    IOptions<SoraWebOptions> webOptions,
    IOptions<ObservabilityOptions>? obsOptions,
    IConfiguration cfg,
    IServiceProvider sp,
    Sora.Data.Core.IDataService? data
) : ControllerBase
{
    private bool CanExposeObservability() => env.IsDevelopment() || (webOptions?.Value?.ExposeObservabilitySnapshot == true);

    [HttpGet("observability")]
    public IActionResult Observability()
    {
        if (!CanExposeObservability()) return NotFound();

        var entry = System.Reflection.Assembly.GetEntryAssembly();
        var serviceName = entry?.GetName().Name ?? "sora-app";
        var serviceVersion = entry?.GetName().Version?.ToString() ?? "0.0.0";
        var serviceInstanceId = Environment.MachineName;

        var opts = obsOptions?.Value ?? new ObservabilityOptions();
        // Respect OTLP env vars if Options not configured
        var otlpEndpoint = opts.Otlp.Endpoint ?? cfg["OTEL_EXPORTER_OTLP_ENDPOINT"];

        var payload = new
        {
            enabled = opts.Enabled && (!string.IsNullOrWhiteSpace(otlpEndpoint) || env.IsDevelopment()),
            resource = new { serviceName, serviceVersion, serviceInstanceId },
            traces = new
            {
                enabled = opts.Traces.Enabled,
                sampleRate = Math.Clamp(opts.Traces.SampleRate, 0.0, 1.0),
                aspNetCoreInstrumentation = opts.Traces.Enabled,
                httpClientInstrumentation = opts.Traces.Enabled,
                exporter = new { type = string.IsNullOrWhiteSpace(otlpEndpoint) ? "none" : "otlp", endpoint = otlpEndpoint },
                currentTraceId = Activity.Current?.TraceId.ToString()
            },
            metrics = new
            {
                enabled = opts.Metrics.Enabled,
                runtimeMetrics = opts.Metrics.Enabled,
                exporter = new { type = string.IsNullOrWhiteSpace(otlpEndpoint) ? "none" : "otlp", endpoint = otlpEndpoint }
            },
            propagation = new[] { "tracecontext", "baggage" },
            headers = new { responseTraceHeader = "Sora-Trace-Id" }
        };

        Response.Headers.CacheControl = "no-store";
        return Ok(payload);
    }

    [HttpGet("aggregates")]
    public IActionResult Aggregates()
    {
        var aggregates = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => SoraWebHelpers.SafeGetTypes(a))
            .Where(t => t.IsClass && !t.IsAbstract)
            .Select(t => new { Type = t, Root = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>)) })
            .Where(x => x.Root is not null)
            .Select(x => new { x.Type, KeyType = x.Root!.GetGenericArguments()[0] })
            .GroupBy(x => x.Type)
            .Select(g => g.First())
            .ToList();

        var items = aggregates.Select(x =>
        {
            var provider = SoraWebHelpers.ResolveProvider(x.Type, sp);

            QueryCapabilities q = QueryCapabilities.None;
            WriteCapabilities w = WriteCapabilities.None;

            if (data is not null)
            {
                try
                {
                    var repo = SoraWebHelpers.GetRepository(sp, data, x.Type, x.KeyType);
                    if (repo is IQueryCapabilities qc) q = qc.Capabilities;
                    if (repo is IWriteCapabilities wc) w = wc.Writes;
                }
                catch { }
            }

            return new
            {
                type = x.Type.FullName,
                key = SoraWebHelpers.ToKeyName(x.KeyType),
                provider,
                query = SoraWebHelpers.EnumFlags(q),
                write = SoraWebHelpers.EnumFlags(w)
            };
        }).ToArray();

        var payload = new
        {
            aggregates = items,
            links = new[] { new { rel = "observability", href = "/.well-known/sora/observability" } }
        };
        return Ok(payload);
    }
}
