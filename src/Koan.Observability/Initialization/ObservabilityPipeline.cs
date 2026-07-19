using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Modules;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Koan.Observability.Infrastructure;

namespace Koan.Observability.Initialization;

internal static class ObservabilityPipeline
{
    public static void Register(IServiceCollection services)
    {
        services.AddKoanOptions<Koan.Core.Observability.ObservabilityOptions>(
            Koan.Core.Infrastructure.Constants.Configuration.Observability.Section);

        var (configuration, environment) = ResolveHost(services);
        var plan = ObservabilityPlan.Compile(configuration, environment);
        services.AddSingleton(plan);
        if (!plan.Active) return;

        var resource = ResourceBuilder.CreateDefault().AddService(
            serviceName: plan.ServiceName,
            serviceVersion: plan.ServiceVersion,
            serviceInstanceId: plan.ServiceInstanceId);
        var builder = services.AddOpenTelemetry();

        if (plan.TracesEnabled)
        {
            builder.WithTracing(traces =>
            {
                traces.SetResourceBuilder(resource)
                    .AddSource(Constants.KoanActivitySources)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(plan.TraceSampleRate)));

                if (plan.OtlpEndpoint is not null)
                {
                    traces.AddOtlpExporter(exporter => ConfigureExporter(exporter, plan));
                }
            });
        }

        if (plan.MetricsEnabled)
        {
            builder.WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resource)
                    .AddMeter(Constants.KoanMeters)
                    .AddRuntimeInstrumentation();

                if (plan.OtlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(exporter => ConfigureExporter(exporter, plan));
                }
            });
        }
    }

    private static void ConfigureExporter(OpenTelemetry.Exporter.OtlpExporterOptions exporter, ObservabilityPlan plan)
    {
        exporter.Endpoint = plan.OtlpEndpoint!;
        if (plan.OtlpHeaders is not null) exporter.Headers = plan.OtlpHeaders;
    }

    private static (IConfiguration? Configuration, IHostEnvironment? Environment) ResolveHost(IServiceCollection services)
    {
        var context = services
            .LastOrDefault(static descriptor => descriptor.ServiceType == typeof(HostBuilderContext))?
            .ImplementationInstance as HostBuilderContext;
        var configuration = context?.Configuration ?? FindInstance<IConfiguration>(services);
        var environment = context?.HostingEnvironment ?? FindInstance<IHostEnvironment>(services);
        return (configuration, environment);
    }

    private static T? FindInstance<T>(IEnumerable<ServiceDescriptor> services) where T : class
        => services
            .LastOrDefault(static descriptor => descriptor.ServiceType == typeof(T))?
            .ImplementationInstance as T;
}
