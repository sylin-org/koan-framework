using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Media.Core.Fonts;

/// <summary>
/// Font registration entry point for apps adding text overlays. Per
/// MEDIA-0004 §7, no fonts are bundled — the application registers
/// each font path it wants exposed to text overlays.
///
/// <example>
/// <code>
/// services.AddKoanFont("default", "wwwroot/fonts/Inter-Regular.ttf");
/// services.AddKoanFont("brand",   "wwwroot/fonts/BrandSans.ttf");
/// </code>
/// </example>
/// </summary>
public static class ServiceCollectionFontExtensions
{
    /// <summary>
    /// Register a font file under <paramref name="name"/> so text
    /// overlays can reference it. Idempotent on the registry side —
    /// re-registering the same name replaces the family.
    /// </summary>
    public static IServiceCollection AddKoanFont(this IServiceCollection services, string name, string path)
    {
        services.TryAddSingleton<KoanFontRegistry>();
        // Register a tiny resolver that runs at first registry resolution
        // to add the font. Keeps the API call composable from anywhere in
        // Program.cs without ordering with respect to AddKoan().
        services.AddSingleton<IFontRegistration>(_ => new FontRegistration(name, path));
        return services;
    }

    internal interface IFontRegistration
    {
        string Name { get; }
        string Path { get; }
    }

    private sealed record FontRegistration(string Name, string Path) : IFontRegistration;

    /// <summary>
    /// Apply all queued <see cref="AddKoanFont"/> registrations to the
    /// registry. Called by the resolver before the first overlay render
    /// so the order between AddKoanFont() and AddKoan() doesn't matter.
    /// </summary>
    public static KoanFontRegistry ApplyPendingFonts(this KoanFontRegistry registry, IServiceProvider services)
    {
        foreach (var reg in services.GetServices<IFontRegistration>())
        {
            try
            {
                registry.Register(reg.Name, reg.Path);
            }
            catch (FileNotFoundException)
            {
                // Skip missing files silently — the overlay will fail later
                // with a 'font not registered' error at render time, which
                // gives a clearer signal than a startup crash.
            }
        }
        return registry;
    }
}
