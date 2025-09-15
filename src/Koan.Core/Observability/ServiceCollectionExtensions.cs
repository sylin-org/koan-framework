using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Koan.Core.Modules;

namespace Koan.Core.Observability;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanObservability(this IServiceCollection services, Action<ObservabilityOptions>? configure = null)
    {
        services.AddKoanOptions<ObservabilityOptions>(Infrastructure.Constants.Configuration.Observability.Section);
        if (configure is not null) services.Configure(configure);

        using var tmp = services.BuildServiceProvider();
        var cfg = tmp.GetService<IConfiguration>();
        var env = tmp.GetService<IHostEnvironment>();
        var opts = tmp.GetService<Microsoft.Extensions.Options.IOptions<ObservabilityOptions>>()?.Value ?? new ObservabilityOptions();

        var enabled = opts.Enabled;
        var otlpEndpoint = opts.Otlp.Endpoint
            ?? Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Otel.Exporter.Otlp.Endpoint, null);
        if (string.IsNullOrWhiteSpace(otlpEndpoint) && (KoanEnv.IsProduction || env?.IsProduction() == true))
        {
            enabled = false;
        }
        if (!enabled) return services;

        var entry = System.Reflection.Assembly.GetEntryAssembly();
        var serviceName = entry?.GetName().Name ?? "Koan-app";
        var serviceVersion = entry?.GetName().Version?.ToString() ?? "0.0.0";
        var resource = ResourceBuilder.CreateDefault().AddService(serviceName: serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName);

        if (opts.Traces.Enabled)
        {
            services.AddOpenTelemetry().WithTracing(b =>
            {
                b.SetResourceBuilder(resource)
                 .AddSource("Koan.Core", "Koan.Data", "Koan.Messaging", "Koan.Web")
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
                            ?? Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Otel.Exporter.Otlp.Headers, null);
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
