using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Sora.Core.Observability;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoraObservability(this IServiceCollection services, Action<ObservabilityOptions>? configure = null)
    {
    services.AddOptions<ObservabilityOptions>().BindConfiguration(Sora.Core.Infrastructure.Constants.Configuration.Observability.Section);
        if (configure is not null) services.Configure(configure);

        using var tmp = services.BuildServiceProvider();
        var cfg = tmp.GetService<IConfiguration>();
    var env = tmp.GetService<IHostEnvironment>();
        var opts = tmp.GetService<Microsoft.Extensions.Options.IOptions<ObservabilityOptions>>()?.Value ?? new ObservabilityOptions();

        var enabled = opts.Enabled;
        var otlpEndpoint = opts.Otlp.Endpoint
            ?? Sora.Core.Configuration.Read<string?>(cfg, Sora.Core.Infrastructure.Constants.Configuration.Otel.Exporter.Otlp.Endpoint, null);
        if (string.IsNullOrWhiteSpace(otlpEndpoint) && (SoraEnv.IsProduction || env?.IsProduction() == true))
        {
            enabled = false;
        }
        if (!enabled) return services;

        var entry = System.Reflection.Assembly.GetEntryAssembly();
        var serviceName = entry?.GetName().Name ?? "sora-app";
        var serviceVersion = entry?.GetName().Version?.ToString() ?? "0.0.0";
        var resource = ResourceBuilder.CreateDefault().AddService(serviceName: serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName);

        if (opts.Traces.Enabled)
        {
            services.AddOpenTelemetry().WithTracing(b =>
            {
                b.SetResourceBuilder(resource)
                 .AddSource("Sora.Core", "Sora.Data", "Sora.Messaging", "Sora.Web")
                 .AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation();

                var rate = Math.Clamp(opts.Traces.SampleRate, 0.0, 1.0);
                b.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(rate)));

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    b.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        var headers = opts.Otlp.Headers
                            ?? Sora.Core.Configuration.Read<string?>(cfg, Sora.Core.Infrastructure.Constants.Configuration.Otel.Exporter.Otlp.Headers, null);
                        if (!string.IsNullOrWhiteSpace(headers)) o.Headers = headers;
                    });
                }
            });
        }

        if (opts.Metrics.Enabled)
        {
            services.AddOpenTelemetry().WithMetrics(b =>
            {
                b.SetResourceBuilder(resource)
                 .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    b.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            });
        }

        return services;
    }
}
