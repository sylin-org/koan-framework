using Koan.Core.Modules;

namespace Koan.Media.Core.Initialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Media.Core.Operators;
using Koan.Media.Core.Options;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Media.Core";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind options from configuration at Koan:Media:Transforms
        services.AddKoanOptions<MediaTransformOptions>("Koan:Media:Transforms");

        services.AddSingleton<IMediaOperator, ResizeOperator>();
        services.AddSingleton<IMediaOperator, RotateOperator>();
        services.AddSingleton<IMediaOperator, TypeConverterOperator>();
        services.AddSingleton<IMediaOperatorRegistry, MediaOperatorRegistry>();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
    }
}
