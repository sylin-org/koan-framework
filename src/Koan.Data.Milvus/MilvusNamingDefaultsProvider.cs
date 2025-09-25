
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Milvus;

public sealed class MilvusNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "milvus";

    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var options = services.GetService<IOptions<MilvusOptions>>()?.Value;
        return new StorageNameResolver.Convention(StorageNamingStyle.EntityType, "_", NameCasing.LowerCase);
    }

    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}
