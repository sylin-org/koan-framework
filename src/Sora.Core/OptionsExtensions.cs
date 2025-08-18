using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sora.Core;

public static class OptionsValidation
{
    public static IServiceCollection AddValidatedOptions<TOptions>(this IServiceCollection services, IConfiguration config, string sectionName, System.Action<TOptions>? postConfigure = null) where TOptions : class, new()
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
