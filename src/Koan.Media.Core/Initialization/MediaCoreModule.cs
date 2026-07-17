using System.Reflection;
using Koan.Core;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Recipes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core.Semantics;
using Koan.Core.Composition;
using Koan.Media.Core.Composition;

namespace Koan.Media.Core.Initialization;

/// <summary>
/// DI registrar for Koan.Media.Core. Wires:
/// <list type="bullet">
///   <item><see cref="RecipesOptions"/> binding from <c>Koan:Media:Recipes</c></item>
///   <item><see cref="IMediaRecipeRegistry"/> singleton scanning loaded assemblies for <c>[MediaRecipe]</c></item>
/// </list>
/// The pipeline itself is stateless and reached via <c>stream.AsMedia()</c>;
/// no DI registration needed for the engine.
/// </summary>
public sealed class MediaCoreModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Bind appsettings recipes
        services.AddOptions<RecipesOptions>()
            .BindConfiguration(RecipesOptions.RootSectionPath);

        // Discover application assemblies for [MediaRecipe] scanning
        services.TryAddSingleton<IMediaRecipeRegistry>(sp =>
        {
            var monitor = sp.GetService<Microsoft.Extensions.Options.IOptionsMonitor<RecipesOptions>>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<MediaRecipeRegistry>>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(IsScannable)
                .ToArray();
            return new MediaRecipeRegistry(assemblies, monitor, logger);
        });
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = services.GetRequiredService<IMediaRecipeRegistry>().All;
        return Task.CompletedTask;
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
    }

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
        => MediaCompositionFacts.Project(composition, services, GetType().FullName ?? Id);

    private static bool IsScannable(Assembly asm)
    {
        if (asm.IsDynamic) return false;
        var name = asm.GetName().Name ?? "";
        // Skip framework + test infra; keep app + Koan modules so recipe-bearing
        // attribute methods in either are discovered.
        if (name.StartsWith("System", StringComparison.Ordinal)) return false;
        if (name.StartsWith("Microsoft", StringComparison.Ordinal)) return false;
        if (name.StartsWith("netstandard", StringComparison.Ordinal)) return false;
        if (name.StartsWith("xunit", StringComparison.Ordinal)) return false;
        if (name.StartsWith("FluentAssertions", StringComparison.Ordinal)) return false;
        return true;
    }
}
