using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Observability;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions;
using Koan.Web.Infrastructure;
using Koan.Web.Options;
using System.Diagnostics;

namespace Koan.Web.Controllers;

[ApiController]
[Produces("application/json")]
[Route(KoanWebConstants.Routes.WellKnownBase)]
public sealed class WellKnownController(
    IHostEnvironment env,
    IOptions<KoanWebOptions> webOptions,
    IOptions<ObservabilityOptions>? obsOptions,
    IConfiguration cfg,
    IServiceProvider sp,
    Koan.Data.Core.IDataService? data
) : ControllerBase
{
    private bool CanExposeObservability() => env.IsDevelopment() || (webOptions?.Value?.ExposeObservabilitySnapshot == true);

    [HttpGet("observability")]
    public IActionResult Observability()
    {
        if (!CanExposeObservability()) return NotFound();

        var entry = System.Reflection.Assembly.GetEntryAssembly();
        var serviceName = entry?.GetName().Name ?? "Koan-app";
        var serviceVersion = entry?.GetName().Version?.ToString() ?? "0.0.0";
        var serviceInstanceId = Environment.MachineName;

        var opts = obsOptions?.Value ?? new ObservabilityOptions();
        // Respect OTLP env vars if Options not configured
        var otlpEndpoint = opts.Otlp.Endpoint
            ?? cfg.Read<string?>(Core.Infrastructure.Constants.Configuration.Otel.Exporter.Otlp.Endpoint, null);

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
            headers = new { responseTraceHeader = KoanWebConstants.Headers.KoanTraceId }
        };

        Response.Headers.CacheControl = KoanWebConstants.Policies.NoStore;
        return Ok(payload);
    }

    [HttpGet(KoanWebConstants.Routes.WellKnownScheduling)]
    public IActionResult Scheduling([FromServices] Koan.Core.Observability.Health.IHealthAggregator aggregator, [FromServices] IOptions<Scheduling.SchedulingOptions>? sched)
    {
        if (!CanExposeObservability()) return NotFound();

        var snap = aggregator.GetSnapshot();
        var tasks = snap.Components
            .Where(c => c.Component.StartsWith("scheduling:task:", StringComparison.OrdinalIgnoreCase))
            .Select(c => new
            {
                id = c.Facts?.TryGetValue("id", out var id) == true ? id : c.Component.Split(':').Last(),
                state = c.Facts?.TryGetValue("state", out var st) == true ? st : c.Status.ToString().ToLowerInvariant(),
                critical = c.Facts?.TryGetValue("critical", out var cr) == true && string.Equals(cr, "true", StringComparison.OrdinalIgnoreCase),
                running = c.Facts?.TryGetValue("running", out var rn) == true ? rn : "0",
                success = c.Facts?.TryGetValue("success", out var sc) == true ? sc : "0",
                fail = c.Facts?.TryGetValue("fail", out var fl) == true ? fl : "0",
                lastError = c.Facts?.TryGetValue("lastError", out var le) == true ? le : null
            })
            .OrderBy(t => t.id)
            .ToArray();

        var payload = new
        {
            enabled = sched?.Value?.Enabled ?? false,
            readinessGate = sched?.Value?.ReadinessGate ?? true,
            tasks,
            links = new[] { new { rel = "observability", href = $"/{KoanWebConstants.Routes.WellKnownBase}/{KoanWebConstants.Routes.WellKnownObservability}" } }
        };

        Response.Headers.CacheControl = KoanWebConstants.Policies.NoStore;
        return Ok(payload);
    }

    [HttpGet("aggregates")]
    public IActionResult Aggregates()
    {
        var aggregates = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => KoanWebHelpers.SafeGetTypes(a))
            .Where(t => t.IsClass && !t.IsAbstract)
            .Select(t => new { Type = t, Root = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>)) })
            .Where(x => x.Root is not null)
            .Select(x => new { x.Type, KeyType = x.Root!.GetGenericArguments()[0] })
            .GroupBy(x => x.Type)
            .Select(g => g.First())
            .ToList();

        var items = aggregates.Select(x =>
        {
            var provider = KoanWebHelpers.ResolveProvider(x.Type, sp);

            QueryCapabilities q = QueryCapabilities.None;
            WriteCapabilities w = WriteCapabilities.None;

            if (data is not null)
            {
                try
                {
                    var repo = KoanWebHelpers.GetRepository(sp, data, x.Type, x.KeyType);
                    if (repo is IQueryCapabilities qc) q = qc.Capabilities;
                    if (repo is IWriteCapabilities wc) w = wc.Writes;
                }
                catch { }
            }

            return new
            {
                type = x.Type.FullName,
                key = KoanWebHelpers.ToKeyName(x.KeyType),
                provider,
                query = KoanWebHelpers.EnumFlags(q),
                write = KoanWebHelpers.EnumFlags(w)
            };
        }).ToArray();

        var payload = new
        {
            aggregates = items,
            links = new[] { new { rel = "observability", href = $"/{KoanWebConstants.Routes.WellKnownBase}/{KoanWebConstants.Routes.WellKnownObservability}" } }
        };
        return Ok(payload);
    }
}
