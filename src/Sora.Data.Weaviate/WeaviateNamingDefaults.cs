using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions.Naming;
using System;

namespace Sora.Data.Weaviate;

public sealed class WeaviateNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "weaviate";

    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        // Weaviate prefers flat names; replace dots with underscores
        var opts = services.GetService<IOptions<WeaviateOptions>>()?.Value;
        // Use EntityType by default with '_' separator to avoid dots in class names
        return new StorageNameResolver.Convention(StorageNamingStyle.EntityType, "_", NameCasing.AsIs);
    }

    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}
