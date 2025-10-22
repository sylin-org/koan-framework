using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Vector.Connector.Weaviate;

public sealed class WeaviateNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "weaviate";

    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        // Weaviate class names cannot contain dots, so use FullNamespace with underscore separator
        // This transforms "S6.SnapVault.Models.PhotoAsset" -> "S6_SnapVault_Models_PhotoAsset"
        var opts = services.GetService<IOptions<WeaviateOptions>>()?.Value;
        return new StorageNameResolver.Convention(StorageNamingStyle.FullNamespace, "_", NameCasing.AsIs);
    }

    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}

