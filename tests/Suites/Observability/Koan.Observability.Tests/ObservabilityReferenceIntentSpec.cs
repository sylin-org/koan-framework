using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Xunit;

namespace Koan.Observability.Tests;

/// <summary>
/// (ARCH-0088 / ARCH-0079) Reference = Intent for the extracted observability package: this test project
/// references <c>Koan.Observability</c>, so its <c>ObservabilityModule</c> runs during real <c>AddKoan()</c>
/// reflective discovery and wires OpenTelemetry — WITHOUT an explicit <c>AddKoanObservability()</c> call.
/// The proof is a live <see cref="TracerProvider"/> in the booted container (the OTel SDK only registers one
/// when <c>AddOpenTelemetry().WithTracing(...)</c> ran). The integration host's default "Test" environment
/// keeps the wiring enabled (production-without-OTLP is the only path that disables it).
/// </summary>
public sealed class ObservabilityReferenceIntentSpec
{
    [Fact]
    public async Task Referencing_Koan_Observability_auto_wires_OpenTelemetry()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        host.Services.GetService<TracerProvider>()
            .Should().NotBeNull("referencing Koan.Observability must auto-wire OpenTelemetry via its registrar (Reference=Intent), with no explicit AddKoanObservability() call");
    }
}
