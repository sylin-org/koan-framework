using Koan.Core;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Web.Controllers;
using Koan.Media.Web.Options;
using Koan.Media.Web.Routing;
using Koan.Media.Web.Sweep;
using Koan.Web.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Media.Web.Initialization;

/// <summary>
/// DI registrar for Koan.Media.Web. Wires:
/// <list type="bullet">
///   <item><see cref="MediaWebOptions"/> binding from <c>Koan:Media:Web</c></item>
///   <item>MVC ApplicationPartManager registration for this assembly so
///   the <see cref="MediaController"/> and <see cref="StorageMediaController{TEntity}"/>
///   routes resolve without consumers having to call
///   <c>AddKoanControllersFrom</c> themselves</item>
///   <item>Default <see cref="IOverlayResolver"/> backed by the registered
///   <see cref="IMediaSource"/> + <see cref="IMediaRecipeRegistry"/> — apps
///   can replace by registering their own implementation before AddKoan()</item>
///   <item>Optional <see cref="MediaDerivationSweepService"/> when
///   <c>Koan:Media:Web:DerivationSweep:Enabled</c> is true</item>
/// </list>
///
/// <para>Applications must still register an
/// <see cref="Koan.Media.Web.Routing.IMediaSource"/> implementation
/// (typically backed by their MediaEntity-derived content layer or by
/// Koan.Storage); the controller has no opinion on where the source
/// bytes live.</para>
///
/// <para>Per MEDIA-0007, derivations are persisted by the
/// <see cref="IMediaSource"/> directly.</para>
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

        // Default overlay resolver backed by IMediaSource. TryAdd so a
        // caller can swap in a custom IOverlayResolver before AddKoan()
        // (e.g. an in-process logo store for brand assets that aren't
        // regular MediaEntity rows).
        services.TryAddSingleton<IOverlayResolver, DefaultOverlayResolver>();

        // Orphan-derivation sweep — MEDIA-0007 §d. Always register the
        // singleton so callers can resolve it for manual sweeps; the hosted
        // background loop only runs when Enabled is true (the service idles
        // out on its own and the gate is rechecked per cycle).
        services.TryAddSingleton<MediaDerivationSweepService>();
        services.AddHostedService(sp => sp.GetRequiredService<MediaDerivationSweepService>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
    }
}
