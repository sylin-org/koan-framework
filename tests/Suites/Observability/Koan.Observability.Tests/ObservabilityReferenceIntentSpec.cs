using System.Diagnostics;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Koan.Observability.Tests;

/// <summary>
/// (ARCH-0088 / ARCH-0079) Reference = Intent for the extracted observability package: this test project
/// references <c>Koan.Observability</c>, so its <c>ObservabilityModule</c> runs during real <c>AddKoan()</c>
/// reflective discovery and wires OpenTelemetry — WITHOUT an explicit <c>AddKoanObservability()</c> call.
/// The integration host's default "Test" environment keeps the wiring enabled; Production without OTLP is inert.
/// </summary>
public sealed class ObservabilityReferenceIntentSpec
{
    [Fact]
    public async Task Reference_auto_wires_one_trace_and_metric_pipeline()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        host.Services.GetService<TracerProvider>()
            .Should().NotBeNull("the package reference must activate tracing without application registration");
        host.Services.GetService<MeterProvider>()
            .Should().NotBeNull("the package reference must activate metrics without application registration");
    }

    [Fact]
    public async Task Pipeline_subscribes_to_every_Koan_source_and_meter()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Observability:Traces:SampleRate", "1")
            .ConfigureServices(services =>
            {
                services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddReader(
                    new PeriodicExportingMetricReader(new AcceptingMetricExporter())));
                services.AddKoan();
            })
            .StartAsync();

        _ = host.Services.GetRequiredService<TracerProvider>();
        _ = host.Services.GetRequiredService<MeterProvider>();

        using var source = new ActivitySource("Koan.Future.Capability");
        using var meter = new Meter("Koan.Future.Capability");
        var counter = meter.CreateCounter<long>("koan.future.operations");

        source.HasListeners().Should().BeTrue("Koan.* is the one automatic tracing subscription boundary");
        counter.Enabled.Should().BeTrue("Koan.* is the one automatic metrics subscription boundary");
    }

    [Fact]
    public async Task Production_without_an_export_destination_is_inert()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .WithEnvironment(Environments.Production)
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        host.Services.GetService<TracerProvider>().Should().BeNull();
        host.Services.GetService<MeterProvider>().Should().BeNull();
    }

    [Fact]
    public async Task Explicit_disable_is_inert()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Observability:Enabled", "false")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        host.Services.GetService<TracerProvider>().Should().BeNull();
        host.Services.GetService<MeterProvider>().Should().BeNull();
    }

    [Theory]
    [InlineData("Koan:Observability:Otlp:Endpoint", "not-a-uri", "absolute HTTP or HTTPS URI")]
    [InlineData("Koan:Observability:Traces:SampleRate", "1.5", "number from 0 to 1")]
    [InlineData("Koan:Observability:Metrics:Enabled", "sometimes", "true' or 'false")]
    public async Task Invalid_configuration_rejects_with_the_exact_correction(string key, string value, string correction)
    {
        Func<Task> start = () => KoanIntegrationHost.Configure()
            .WithSetting(key, value)
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var failure = await start.Should().ThrowAsync<KoanBootException>()
            .WithMessage($"*{key}*{correction}*");

        failure.Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Standard_OpenTelemetry_customization_composes_into_the_same_provider()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Observability:Traces:SampleRate", "1")
            .ConfigureServices(services =>
            {
                services.AddOpenTelemetry().WithTracing(traces => traces.AddSource("Application.Custom"));
                services.AddKoan();
            })
            .StartAsync();

        host.Services.GetServices<TracerProvider>().Should().ContainSingle();
        using var appSource = new ActivitySource("Application.Custom");
        using var koanSource = new ActivitySource("Koan.Custom");
        appSource.HasListeners().Should().BeTrue();
        koanSource.HasListeners().Should().BeTrue();
    }

    private sealed class AcceptingMetricExporter : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch) => ExportResult.Success;
    }
}
