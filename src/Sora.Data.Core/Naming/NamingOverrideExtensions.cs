using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Core.Naming;

public static class NamingOverrideExtensions
{
    /// <summary>
    /// Globally override storage naming policy with a delegate. This replaces the DI IStorageNameResolver.
    /// </summary>
    public static IServiceCollection OverrideStorageNaming(this IServiceCollection services, Func<Type, StorageNameResolver.Convention, string?> resolver)
    {
        return services.AddSingleton<IStorageNameResolver>(_ => new DelegatingStorageNameResolver(resolver));
    }

    /// <summary>
    /// Register optional global fallback options that apply when no provider defaults are present.
    /// </summary>
    public static IServiceCollection ConfigureGlobalNamingFallback(this IServiceCollection services, Action<NamingFallbackOptions> configure)
    {
        services.AddOptions<NamingFallbackOptions>().Configure(configure);
        return services;
    }
}
