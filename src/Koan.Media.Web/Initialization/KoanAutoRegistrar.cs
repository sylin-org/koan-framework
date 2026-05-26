using Koan.Core;
using Koan.Media.Web.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Media.Web.Initialization;

/// <summary>
/// DI registrar for Koan.Media.Web. Binds <see cref="MediaWebOptions"/>
/// from <c>Koan:Media:Web</c>. The MediaController is discovered by
/// ASP.NET Core's standard controller scan; no explicit registration
/// needed beyond the host having added <c>AddControllers()</c>.
///
/// Applications must register an <see cref="Koan.Media.Web.Routing.IMediaSource"/>
/// implementation (typically backed by their MediaEntity-derived
/// content layer or by Koan.Storage).
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Media.Web";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<MediaWebOptions>()
            .BindConfiguration(MediaWebOptions.SectionPath);
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
    }
}
