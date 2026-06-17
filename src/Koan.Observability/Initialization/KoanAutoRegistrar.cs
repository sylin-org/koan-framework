using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Observability;
using Koan.Core.Provenance;

namespace Koan.Observability.Initialization;

/// <summary>
/// Reference = Intent (ARCH-0088): referencing the <c>Koan.Observability</c> package auto-enables the
/// OpenTelemetry wiring (traces + metrics + OTLP export) at boot. Configure via the
/// <c>Koan:Observability</c> config section (or <c>OTEL_*</c> env vars). The public
/// <c>AddKoanObservability()</c> method stays available and is idempotent with this registrar (a second
/// call does not re-wire the pipeline). The health/probe primitives stay in <c>Koan.Core</c>.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Observability";

    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services) => services.AddKoanObservability();

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(ModuleVersion);
}
