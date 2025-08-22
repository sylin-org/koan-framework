namespace Sora.Data.Abstractions.Naming;

/// <summary>
/// Supplies provider-specific naming defaults and optional adapter-level override.
/// Implemented by each adapter to provide its convention and adapter override delegate.
/// </summary>
public interface INamingDefaultsProvider
{
    /// <summary>Provider key (e.g., "mongo", "sqlite").</summary>
    string Provider { get; }

    /// <summary>Default naming convention for this provider.</summary>
    StorageNameResolver.Convention GetConvention(IServiceProvider services);

    /// <summary>Optional adapter-level override (e.g., MongoOptions.CollectionName).</summary>
    Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}
