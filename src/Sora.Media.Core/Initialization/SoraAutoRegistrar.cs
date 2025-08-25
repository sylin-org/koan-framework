using Sora.Core.Modules;

namespace Sora.Media.Core.Initialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Media.Core.Operators;
using Sora.Media.Core.Options;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Media.Core";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind options from configuration at Sora:Media:Transforms
        services.AddSoraOptions<MediaTransformOptions>("Sora:Media:Transforms");

        services.AddSingleton<IMediaOperator, ResizeOperator>();
        services.AddSingleton<IMediaOperator, RotateOperator>();
        services.AddSingleton<IMediaOperator, TypeConverterOperator>();
        services.AddSingleton<IMediaOperatorRegistry, MediaOperatorRegistry>();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
    }
}
