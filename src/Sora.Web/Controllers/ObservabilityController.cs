using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core.Observability;

namespace Sora.Web.Controllers;

[ApiController]
[Produces("application/json")]
[Route(".well-known/sora/[controller]")]
public sealed class ObservabilityController(
    IOptions<SoraWebOptions> webOptions,
    IOptions<ObservabilityOptions>? obsOptions,
    IConfiguration cfg,
    IHostEnvironment env
) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var allow = env.IsDevelopment() || (webOptions?.Value?.ExposeObservabilitySnapshot == true);
        if (!allow) return NotFound();

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
                aspNetCoreInstrumentation = opts.Traces.Enabled, // wired when enabled
                httpClientInstrumentation = opts.Traces.Enabled,  // wired when enabled
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

        Response.Headers["Cache-Control"] = "no-store";
        return Ok(payload);
    }
}
