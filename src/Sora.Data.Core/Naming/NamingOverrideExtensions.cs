using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Core.Naming;

/// <summary>
/// Options for global fallback naming when no provider-specific defaults are registered.
/// Bind from configuration: Sora:Data:Naming:{Style,Separator,Casing}.
/// </summary>
public sealed class NamingFallbackOptions
{
    public StorageNamingStyle Style { get; set; } = StorageNamingStyle.EntityType;
    public string Separator { get; set; } = ".";
    public NameCasing Casing { get; set; } = NameCasing.AsIs;
}

internal sealed class DelegatingStorageNameResolver : IStorageNameResolver
{
    private readonly Func<Type, StorageNameResolver.Convention, string?> _override;
    private readonly IStorageNameResolver _inner;

    public DelegatingStorageNameResolver(Func<Type, StorageNameResolver.Convention, string?> @override)
    {
        _override = @override;
        _inner = new DefaultStorageNameResolver();
    }

    public string Resolve(Type entityType, StorageNameResolver.Convention defaults)
        => _override(entityType, defaults) ?? _inner.Resolve(entityType, defaults);
}

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
