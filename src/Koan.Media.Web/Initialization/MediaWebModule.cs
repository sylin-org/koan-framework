using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Web.Controllers;
using Koan.Media.Web.Options;
using Koan.Media.Web.Routing;
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
public sealed class MediaWebModule : KoanModule
{
    private MediaSourceDiscovery.Selection? _sourceSelection;

    public override void Register(IServiceCollection services)
    {
        services.AddOptions<MediaWebOptions>()
            .BindConfiguration(MediaWebOptions.SectionPath);

        // Make the MediaController + StorageMediaController<T> routes
        // visible to ASP.NET's controller discovery. Required because
        // class libraries that ship controllers aren't picked up by the
        // default scan against the entry assembly's references.
        services.AddKoanControllersFrom<MediaController>();

        _sourceSelection = MediaSourceDiscovery.RegisterDefault(services);

        // Default overlay resolver backed by IMediaSource. TryAdd so a caller can swap in a custom
        // IOverlayResolver before AddKoan() (e.g. an in-process logo store for brand assets that aren't
        // regular MediaEntity rows). Registered only once a source exists, because the resolver requires
        // one: a bare reference with no MediaEntity must compose inertly, and an unsatisfiable descriptor
        // fails container validation before any of it runs.
        if (_sourceSelection.SourceRegistered)
        {
            services.TryAddSingleton<IOverlayResolver, DefaultOverlayResolver>();
        }
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Resolve only when a source is meant to exist, so an ambiguous choice still fails at host start
        // with its correction. With no MediaEntity at all the module stays inert rather than stopping a
        // host that simply has no media yet.
        if (_sourceSelection?.SourceRegistered != false)
        {
            _ = services.GetRequiredService<IMediaSource>();
        }
        return Task.CompletedTask;
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        if (_sourceSelection is not null)
        {
            module.AddNote(_sourceSelection.Summary);
        }
    }
}
