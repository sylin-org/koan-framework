using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;

namespace Koan.Core;

public static class OptionsValidation
{
    /// <summary>
    /// Obsolete. Use AddKoanOptions&lt;TOptions&gt;() from Koan.Core.OptionsExtensions instead.
    /// </summary>
    [Obsolete("Use AddKoanOptions<TOptions>() from Koan.Core.OptionsExtensions.")]
    public static IServiceCollection AddValidatedOptions<TOptions>(this IServiceCollection services, IConfiguration config, string sectionName, Action<TOptions>? postConfigure = null) where TOptions : class, new()
    {
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .Validate(o => o is not null, "Options binding returned null");
        if (postConfigure is not null) services.PostConfigure(postConfigure);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TOptions>>().Value);
        return services;
    }
}
