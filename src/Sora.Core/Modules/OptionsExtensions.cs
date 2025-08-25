using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sora.Core;

// Centralized helpers for binding and validating options across modules
public static class OptionsExtensions
{
    // Bind options from a configuration path (optional) and enforce DataAnnotations + ValidateOnStart
    public static OptionsBuilder<TOptions> AddSoraOptions<TOptions>(this IServiceCollection services, string? configPath = null, bool validateOnStart = true)
        where TOptions : class
    {
        var builder = services.AddOptions<TOptions>();
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            // Bind lazily via IConfigureOptions to avoid a hard dependency on IConfiguration in DI.
            // If IConfiguration is present, bind from the specified section; otherwise, skip binding (defaults apply).
            services.AddSingleton<IConfigureOptions<TOptions>>(sp =>
                new ConfigureNamedOptions<TOptions>(Options.DefaultName, opts =>
                {
                    var cfg = sp.GetService<IConfiguration>();
                    cfg?.GetSection(configPath).Bind(opts);
                }));
        }
        builder.ValidateDataAnnotations();
        if (validateOnStart)
        {
            builder.ValidateOnStart();
        }
        return builder;
    }

    // Convenience: bind from section and allow post-configure tweaks
    public static OptionsBuilder<TOptions> AddSoraOptions<TOptions>(this IServiceCollection services, IConfiguration cfg, string sectionPath, Action<TOptions>? postConfigure = null, bool validateOnStart = true)
        where TOptions : class
    {
        var builder = services.AddOptions<TOptions>().Bind(cfg.GetSection(sectionPath));
        builder.ValidateDataAnnotations();
        if (postConfigure is not null)
        {
            services.PostConfigure(postConfigure);
        }
        if (validateOnStart) builder.ValidateOnStart();
        return builder;
    }
}
