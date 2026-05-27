using Koan.Core;
using Koan.Media.Web.Controllers;
using Koan.Media.Web.Options;
using Koan.Web.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Media.Web.Initialization;

/// <summary>
/// DI registrar for Koan.Media.Web. Wires:
/// <list type="bullet">
///   <item><see cref="MediaWebOptions"/> binding from <c>Koan:Media:Web</c></item>
///   <item>MVC ApplicationPartManager registration for this assembly so
///   the <see cref="MediaController"/> and <see cref="StorageMediaController{TEntity}"/>
///   routes resolve without consumers having to call
///   <c>AddKoanControllersFrom</c> themselves</item>
/// </list>
///
/// <para>Applications must still register an
/// <see cref="Koan.Media.Web.Routing.IMediaSource"/> implementation
/// (typically backed by their MediaEntity-derived content layer or by
/// Koan.Storage); the controller has no opinion on where the source
/// bytes live.</para>
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Media.Web";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<MediaWebOptions>()
            .BindConfiguration(MediaWebOptions.SectionPath);

        // Make the MediaController + StorageMediaController<T> routes
        // visible to ASP.NET's controller discovery. Required because
        // class libraries that ship controllers aren't picked up by the
        // default scan against the entry assembly's references.
        services.AddKoanControllersFrom<MediaController>();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
    }
}
