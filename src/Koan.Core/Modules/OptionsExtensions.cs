using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Koan.Core.Modules;

// Centralized helpers for binding and validating options across modules
public static class OptionsExtensions
{
    // Bind options from a configuration path (optional) and enforce DataAnnotations + ValidateOnStart
    public static OptionsBuilder<TOptions> AddKoanOptions<TOptions>(this IServiceCollection services, string? configPath = null, bool validateOnStart = true)
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

    // Overload with configurator type - eliminates manual TryAddEnumerable boilerplate
    public static OptionsBuilder<TOptions> AddKoanOptions<TOptions, TConfigurator>(
        this IServiceCollection services,
        string? configPath = null,
        bool validateOnStart = true,
        ServiceLifetime configuratorLifetime = ServiceLifetime.Singleton)
        where TOptions : class
        where TConfigurator : class, IConfigureOptions<TOptions>
    {
        // Register options with binding and validation
        var builder = services.AddKoanOptions<TOptions>(configPath, validateOnStart);

        // Register configurator using TryAddEnumerable (preserves multi-registration pattern)
        var descriptor = configuratorLifetime switch
        {
            ServiceLifetime.Singleton => ServiceDescriptor.Singleton<IConfigureOptions<TOptions>, TConfigurator>(),
            ServiceLifetime.Scoped => ServiceDescriptor.Scoped<IConfigureOptions<TOptions>, TConfigurator>(),
            ServiceLifetime.Transient => ServiceDescriptor.Transient<IConfigureOptions<TOptions>, TConfigurator>(),
            _ => throw new ArgumentException($"Invalid lifetime: {configuratorLifetime}", nameof(configuratorLifetime))
        };

        services.TryAddEnumerable(descriptor);

        return builder;
    }

    // Convenience: bind from section and allow post-configure tweaks
    public static OptionsBuilder<TOptions> AddKoanOptions<TOptions>(this IServiceCollection services, IConfiguration cfg, string sectionPath, Action<TOptions>? postConfigure = null, bool validateOnStart = true)
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

    // --- Layering helpers (encode intent; establish predictable precedence) ---

    // 1) Provider defaults - register early so later layers can override
    public static OptionsBuilder<TOptions> WithProviderDefaults<TOptions>(this OptionsBuilder<TOptions> builder, Action<TOptions> configure)
        where TOptions : class
    {
        builder.Services.Configure(configure);
        return builder;
    }

    // 2) Recipe defaults - soft defaults after provider defaults
    public static OptionsBuilder<TOptions> WithRecipeDefaults<TOptions>(this OptionsBuilder<TOptions> builder, Action<TOptions> configure)
        where TOptions : class
    {
        builder.Services.Configure(configure);
        return builder;
    }

    // 3) Bind from configuration - appsettings/env usually wins over defaults
    public static OptionsBuilder<TOptions> BindFromConfiguration<TOptions>(this OptionsBuilder<TOptions> builder, IConfigurationSection section)
        where TOptions : class
    {
        builder.Bind(section);
        return builder;
    }

    // 4) Code overrides - host-level adjustments registered late
    public static OptionsBuilder<TOptions> WithCodeOverrides<TOptions>(this OptionsBuilder<TOptions> builder, Action<TOptions> configure)
        where TOptions : class
    {
        builder.Services.Configure(configure);
        return builder;
    }

    // 5) Recipe forced overrides - last-wins via PostConfigure; guard usage via config flags
    public static OptionsBuilder<TOptions> WithRecipeForcedOverrides<TOptions>(this OptionsBuilder<TOptions> builder, Action<TOptions> postConfigure)
        where TOptions : class
    {
        builder.Services.PostConfigure(postConfigure);
        return builder;
    }
}
